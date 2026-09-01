using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DriftDeck.Models;
using DriftDeck.Services;
using Microsoft.Web.WebView2.Core;

namespace DriftDeck.Controls;

public partial class PanelHost : UserControl, IDisposable
{
    private const double MinEffectiveOpacity = 0.2;

    private Point _dragStart;
    private double _startX;
    private double _startY;
    private bool _dragging;
    private bool _initialized;
    private bool _disposed;
    private double _globalOpacityFactor = 1;
    private bool _suppressAddressUpdate;
    private readonly ObservableCollection<ChecklistItem> _checklist = [];
    private DispatcherTimer? _timerTick;
    private TimerState _timer;
    private bool _timerFinished;
    private readonly ImageStore _imageStore = new();

    public PanelDefinition Definition { get; }

    /// <summary>Rectangles this panel should snap against (other panels and the dock).</summary>
    public Func<PanelHost, IReadOnlyList<Rect>>? SnapRectsProvider { get; set; }

    public event EventHandler? CloseRequested;
    public event EventHandler? PanelChanged;
    public event EventHandler? Activated;

    public ICommand FocusAddressCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ClosePanelCommand { get; }

    public PanelHost(PanelDefinition definition)
    {
        Definition = definition;
        InitializeComponent();

        FocusAddressCommand = new RelayCommand(FocusAddress);
        ReloadCommand = new RelayCommand(Reload);
        ClosePanelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        // InputBindings live outside the visual tree, so they are wired up in code.
        InputBindings.Add(new KeyBinding(FocusAddressCommand, Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ReloadCommand, Key.R, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ClosePanelCommand, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(ToggleShade), Key.M, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => ApplyContentScale(Definition.ContentScale + 0.1, true)),
            Key.OemPlus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => ApplyContentScale(Definition.ContentScale - 0.1, true)),
            Key.OemMinus, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Browser.CoreWebView2?.GoBack()),
            Key.Left, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Browser.CoreWebView2?.GoForward()),
            Key.Right, ModifierKeys.Alt));

        Width = Math.Max(MinWidth, definition.Width);
        Height = Math.Max(MinHeight, definition.Height);
        Opacity = 1;
        OpacitySlider.Value = Math.Clamp(definition.Opacity, 0.35, 1);
        TitleText.Text = definition.Title;
        ApplyContentScale(Math.Clamp(definition.ContentScale, 0.5, 1.5), false);

        if (definition.Kind == PanelKind.Browser)
        {
            // WebView2 accepts only fully opaque or fully transparent here, so the token's
            // alpha is dropped; panel-level translucency comes from the window's Opacity.
            var deep = (Color)FindResource("SurfaceDeepColor");
            Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, deep.R, deep.G, deep.B);
            AddressBox.Text = definition.Url;
            Loaded += BrowserPanel_OnLoaded;
        }
        else if (definition.Kind == PanelKind.ImagePin)
        {
            HideBrowserChrome();
            ImageSurface.Visibility = Visibility.Visible;
            InputBindings.Add(new KeyBinding(new RelayCommand(PasteImage), Key.V, ModifierKeys.Control));
            LoadImage(definition.ImagePath);
            Loaded += (_, _) => Focus();
        }
        else if (definition.Kind == PanelKind.Timer)
        {
            HideBrowserChrome();
            TimerSurface.Visibility = Visibility.Visible;
            _timer = new TimerState(
                Math.Clamp(definition.TimerDurationSeconds, 1, TimerState.MaximumDurationSeconds),
                Math.Max(0, definition.TimerRemainingSeconds),
                definition.TimerEndUtc);
            TimerDurationBox.Text = TimerState.Format(_timer.DurationSeconds);
            // A timer left running is still running: its end instant was persisted, so the
            // readout picks up wherever the clock has moved to rather than at a stale count.
            UpdateTimerDisplay();
            if (_timer.IsRunning)
            {
                StartTimerTicking();
            }
        }
        else if (definition.Kind == PanelKind.Checklist)
        {
            HideBrowserChrome();
            ChecklistSurface.Visibility = Visibility.Visible;
            definition.Items ??= [];
            foreach (var item in definition.Items)
            {
                _checklist.Add(item);
            }

            // The collection is the source of truth the panel binds to; the definition's list
            // is rewritten from it whenever it changes, so persistence needs no separate step.
            _checklist.CollectionChanged += Checklist_OnCollectionChanged;
            foreach (var item in _checklist)
            {
                item.PropertyChanged += ChecklistItem_OnPropertyChanged;
            }

            ChecklistItems.ItemsSource = _checklist;
            UpdateChecklistSummary();
            Loaded += (_, _) => ChecklistAddBox.Focus();
        }
        else
        {
            HideBrowserChrome();
            NotesSurface.Visibility = Visibility.Visible;
            NotesBox.Text = definition.Notes;
            UpdateNotesPlaceholder();
            Loaded += (_, _) => NotesBox.Focus();
        }

        _initialized = true;
    }

    // ============================ Browser ============================

    private async void BrowserPanel_OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= BrowserPanel_OnLoaded;
        try
        {
            await Browser.EnsureCoreWebView2Async();
            var core = Browser.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsSwipeNavigationEnabled = false;
            Browser.ZoomFactor = Definition.ContentScale;

            core.HistoryChanged += (_, _) => UpdateNavigationState();
            core.NavigationStarting += Core_OnNavigationStarting;
            core.NavigationCompleted += Core_OnNavigationCompleted;
            core.SourceChanged += (_, _) => SyncAddressFromBrowser();
            core.DocumentTitleChanged += Core_OnDocumentTitleChanged;
            // Keep pop-ups inside the panel: a bare WebView2 window has no chrome and cannot be closed.
            core.NewWindowRequested += Core_OnNewWindowRequested;

            Navigate(Definition.Url);
        }
        catch (Exception exception)
        {
            Browser.Visibility = Visibility.Collapsed;
            BrowserErrorRetry.Visibility = Visibility.Collapsed;
            ShowBrowserError(
                "WebView2 is not available",
                $"Install or repair the Microsoft Edge WebView2 Runtime, then reopen this panel.\n\n{exception.Message}");
        }
    }

    private void Core_OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        HideBrowserError();
        StartLoadingBar();
    }

    private void Core_OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        StopLoadingBar();
        UpdateNavigationState();
        if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
        {
            return;
        }

        ShowBrowserError("This page could not be loaded", DescribeWebError(e.WebErrorStatus));
    }

    private void Core_OnDocumentTitleChanged(object? sender, object e)
    {
        if (Definition.HasCustomTitle)
        {
            return;
        }

        var title = Browser.CoreWebView2?.DocumentTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        ApplyPanelTitle(title.Trim());
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets a title the panel worked out for itself — a page title, or an image's file name.
    /// A name the user typed is checked for by the caller and always wins.
    /// </summary>
    private void ApplyPanelTitle(string title)
    {
        Definition.Title = title;
        TitleText.Text = title;
        SyncWindowTitle();
    }

    private void Core_OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        Navigate(e.Uri);
    }

    private static string DescribeWebError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "The address could not be resolved. Check the spelling.",
        CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.CannotConnect =>
            "No connection to that server.",
        CoreWebView2WebErrorStatus.Timeout => "The server took too long to respond.",
        CoreWebView2WebErrorStatus.ServerUnreachable => "That server is unreachable.",
        CoreWebView2WebErrorStatus.CertificateExpired or CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect
            or CoreWebView2WebErrorStatus.CertificateIsInvalid => "The site's security certificate is not valid.",
        _ => $"The request failed ({status})."
    };

    private void ShowBrowserError(string title, string detail)
    {
        BrowserErrorTitle.Text = title;
        BrowserErrorDetail.Text = detail;
        BrowserError.Visibility = Visibility.Visible;
    }

    private void HideBrowserError() => BrowserError.Visibility = Visibility.Collapsed;

    private void UpdateNavigationState()
    {
        var core = Browser.CoreWebView2;
        BackButton.IsEnabled = core?.CanGoBack == true;
        ForwardButton.IsEnabled = core?.CanGoForward == true;
    }

    private void SyncAddressFromBrowser()
    {
        var source = Browser.CoreWebView2?.Source;
        if (string.IsNullOrEmpty(source) || AddressBox.IsKeyboardFocusWithin)
        {
            return;
        }

        _suppressAddressUpdate = true;
        AddressBox.Text = source;
        _suppressAddressUpdate = false;
        Definition.Url = source;
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Navigate(string input)
    {
        var candidate = input.Trim();
        if (candidate.Length == 0)
        {
            return;
        }

        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ShowBrowserError("That is not a web address", "Enter an http or https address, such as example.com.");
            return;
        }

        HideBrowserError();
        AddressBox.Text = uri.AbsoluteUri;
        Definition.Url = uri.AbsoluteUri;
        Browser.Source = uri;
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Reload()
    {
        if (Definition.Kind != PanelKind.Browser)
        {
            return;
        }

        HideBrowserError();
        if (Browser.CoreWebView2 is null)
        {
            Navigate(AddressBox.Text);
            return;
        }

        Browser.CoreWebView2.Reload();
    }

    private void FocusAddress()
    {
        if (Definition.Kind != PanelKind.Browser)
        {
            return;
        }

        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    // Indeterminate sweep: the panel is small, so a looping wipe reads faster than a percentage.
    private void StartLoadingBar()
    {
        if (!Motion.Enabled)
        {
            LoadingBar.Opacity = 1;
            LoadingScale.ScaleX = 1;
            return;
        }

        LoadingBar.Opacity = 1;
        LoadingScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0, 1, Motion.Slow)
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = Motion.EaseInOut
            });
    }

    private void StopLoadingBar()
    {
        LoadingScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        LoadingScale.ScaleX = 1;
        Motion.Hold(LoadingBar, OpacityProperty, 0, Motion.Base);
    }

    private void HideBrowserChrome()
    {
        ToolbarRow.Height = new GridLength(0);
        BrowserToolbar.Visibility = Visibility.Collapsed;
        LoadingBar.Visibility = Visibility.Collapsed;
        BrowserSurface.Visibility = Visibility.Collapsed;
    }

    // ============================ Image ============================

    /// <summary>
    /// Points the panel at a file and shows it. A path that no longer resolves is reported in
    /// the caption rather than cleared: the user chose that file, and silently forgetting it
    /// would hide the fact that something moved.
    /// </summary>
    private void LoadImage(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim();
        Definition.ImagePath = trimmed;

        BitmapImage? bitmap = null;
        var exists = trimmed.Length > 0 && File.Exists(trimmed);
        if (exists)
        {
            try
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                // OnLoad, so the file is not held open. A pinned image the user could not then
                // move or delete would be worse than one that goes missing.
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.UriSource = new Uri(Path.GetFullPath(trimmed));
                bitmap.EndInit();
                bitmap.Freeze();
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException
                                                  or UriFormatException or ArgumentException)
            {
                bitmap = null;
                exists = false;
            }
        }

        ImageView.Source = bitmap;
        var hasImage = bitmap is not null;
        ImageScroller.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        ImageEmptyState.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
        ImageReplaceButton.Visibility = trimmed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ImageOpenButton.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        ImageCaption.Text = ImagePin.Describe(trimmed, exists);

        if (trimmed.Length > 0 && !exists)
        {
            ImageEmptyTitle.Text = "That image is gone";
            ImageEmptyDetail.Text = "The file moved or was deleted. Drop another, paste one, or choose a file.";
        }
        else
        {
            ImageEmptyTitle.Text = "Drop an image here";
            ImageEmptyDetail.Text = "Or paste one with Ctrl+V, or choose a file.";
        }

        ApplyImageScale();
        // The file name is a better panel title than the generic one, but it never overrides a
        // name the user typed.
        if (hasImage && !Definition.HasCustomTitle)
        {
            var name = Path.GetFileNameWithoutExtension(trimmed);
            ApplyPanelTitle(string.IsNullOrWhiteSpace(name) ? "Image" : name);
        }
    }

    private void SetImage(string path)
    {
        LoadImage(path);
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ImageChooseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var filter = string.Join(";", ImagePin.SupportedExtensions.Select(extension => $"*{extension}"));
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an image",
            Filter = $"Images ({filter})|{filter}|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            SetImage(dialog.FileName);
        }
    }

    private void ImageOpenButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(Definition.ImagePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(Definition.ImagePath) { UseShellExecute = true });
    }

    private void ImageSurface_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedImagePath(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ImageSurface_OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedImagePath(e) is { } path)
        {
            SetImage(path);
        }
    }

    private static string? DroppedImagePath(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
            ? ImagePin.FirstSupportedFile(e.Data.GetData(DataFormats.FileDrop) as string[])
            : null;

    /// <summary>
    /// A pasted image is the one case where DriftDeck has to own a file: the clipboard hands
    /// over pixels with no path behind them.
    /// </summary>
    private void PasteImage()
    {
        if (Definition.Kind != PanelKind.ImagePin)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string?>().OfType<string>();
                if (ImagePin.FirstSupportedFile(files) is { } path)
                {
                    SetImage(path);
                    return;
                }
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                SetImage(_imageStore.Save(image));
            }
        }
        catch (Exception exception) when (exception is ExternalException or IOException)
        {
            // Another process can hold the clipboard open. Nothing is lost by ignoring the
            // paste; the user can press it again.
        }
    }

    private void ApplyImageScale()
    {
        if (ImageView.Source is not BitmapSource source)
        {
            return;
        }

        // Uniform stretch already fits the panel. Content scale multiplies that, and the scroll
        // viewer supplies panning once the image is larger than the panel.
        var scale = Definition.ContentScale;
        if (Math.Abs(scale - 1) < 0.01)
        {
            ImageView.Width = double.NaN;
            ImageView.Height = double.NaN;
            return;
        }

        ImageView.Width = source.PixelWidth * scale;
        ImageView.Height = source.PixelHeight * scale;
    }

    // ============================ Timer ============================

    private void TimerStartButton_OnClick(object sender, RoutedEventArgs e)
    {
        var now = DateTime.UtcNow;
        _timer = _timer.IsRunning ? _timer.Pause(now) : _timer.Start(now);
        if (_timer.IsRunning)
        {
            StartTimerTicking();
        }
        else
        {
            StopTimerTicking();
        }

        CommitTimer();
    }

    private void TimerResetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _timer = _timer.Reset();
        StopTimerTicking();
        CommitTimer();
    }

    private void TimerDurationBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        ApplyTimerDuration();
    }

    private void TimerDurationBox_OnLostFocus(object sender, RoutedEventArgs e) => ApplyTimerDuration();

    /// <summary>
    /// Rejected input is rewritten to the length still in force rather than left sitting there
    /// in red. The box is two characters wide in practice; an error state would cost more room
    /// than the mistake is worth.
    /// </summary>
    private void ApplyTimerDuration()
    {
        if (TimerState.TryParseDuration(TimerDurationBox.Text, out var seconds) &&
            seconds != _timer.DurationSeconds)
        {
            _timer = _timer.WithDuration(seconds);
            StopTimerTicking();
            CommitTimer();
        }

        TimerDurationBox.Text = TimerState.Format(_timer.DurationSeconds);
    }

    private void StartTimerTicking()
    {
        // Quarter-second, so the readout never sits a whole second behind the clock. It only
        // touches the label — persistence happens on transitions, not on ticks.
        _timerTick ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) => OnTimerTick(), Dispatcher);
        _timerTick.Start();
    }

    private void StopTimerTicking() => _timerTick?.Stop();

    private void OnTimerTick()
    {
        UpdateTimerDisplay();
        if (_timer.IsRunning && _timer.IsFinishedAt(DateTime.UtcNow))
        {
            // Stop the clock but leave the state at zero, so the panel reads as finished until
            // the user acts. An overlay must never steal focus or make noise over a game, so
            // reaching zero is reported by the readout alone.
            _timer = _timer.Pause(DateTime.UtcNow);
            StopTimerTicking();
            CommitTimer();
        }
    }

    private void CommitTimer()
    {
        Definition.TimerDurationSeconds = _timer.DurationSeconds;
        Definition.TimerRemainingSeconds = _timer.RemainingSeconds;
        Definition.TimerEndUtc = _timer.EndUtc;
        UpdateTimerDisplay();
        if (_initialized)
        {
            PanelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateTimerDisplay()
    {
        var remaining = _timer.RemainingAt(DateTime.UtcNow);
        TimerReadout.Text = TimerState.Format(remaining);
        _timerFinished = remaining == 0;
        TimerReadout.Foreground = (Brush)FindResource(_timerFinished ? "WarnBrush" : "TextBrush");
        TimerStartButton.Content = _timer.IsRunning ? "Pause" : "Start";
        TimerResetButton.IsEnabled = _timer.IsRunning || remaining != _timer.DurationSeconds;
    }

    // ============================ Checklist ============================

    private void ChecklistAddBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!Checklist.TryCreate(ChecklistAddBox.Text, out var item))
        {
            return;
        }

        _checklist.Add(item);
        // Cleared rather than left selected: the next thing typed is nearly always another item.
        ChecklistAddBox.Clear();
    }

    /// <summary>
    /// Enter in a row commits the edit and returns to the add box, so a burst of typing never
    /// has to reach for the mouse. Escape does the same without treating it as a commit.
    /// </summary>
    private void ChecklistItemBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        e.Handled = true;
        ChecklistAddBox.Focus();
    }

    private void ChecklistRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChecklistItem item })
        {
            _checklist.Remove(item);
        }
    }

    private void ChecklistClearDone_OnClick(object sender, RoutedEventArgs e)
    {
        Checklist.ClearCompleted(_checklist);
        ChecklistAddBox.Focus();
    }

    private void Checklist_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<ChecklistItem>() ?? [])
        {
            item.PropertyChanged -= ChecklistItem_OnPropertyChanged;
        }

        foreach (var item in e.NewItems?.OfType<ChecklistItem>() ?? [])
        {
            item.PropertyChanged += ChecklistItem_OnPropertyChanged;
        }

        CommitChecklist();
    }

    private void ChecklistItem_OnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        CommitChecklist();

    private void CommitChecklist()
    {
        Definition.Items = [.. _checklist];
        UpdateChecklistSummary();
        if (_initialized)
        {
            PanelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateChecklistSummary()
    {
        ChecklistSummary.Text = Checklist.Summary(_checklist);
        ChecklistClearDone.Visibility = _checklist.Any(item => item.IsDone)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ============================ Geometry ============================

    public void CaptureDefinition()
    {
        if (Window.GetWindow(this) is not PanelWindow panelWindow)
        {
            return;
        }

        Definition.X = panelWindow.Left;
        Definition.Y = panelWindow.Top;
        Definition.Width = panelWindow.ActualWidth;
        // A rolled-up panel is 30px tall; persisting that would lose the real size.
        Definition.Height = panelWindow.IsShaded ? Definition.RestoreHeight : panelWindow.ActualHeight;
        Definition.Opacity = OpacitySlider.Value;
        Definition.Notes = NotesBox.Text;
        Definition.Items = [.. _checklist];
        Definition.TimerDurationSeconds = _timer.DurationSeconds;
        Definition.TimerRemainingSeconds = _timer.RemainingSeconds;
        Definition.TimerEndUtc = _timer.EndUtc;
        if (Definition.Kind == PanelKind.Browser)
        {
            Definition.Url = AddressBox.Text;
        }
    }

    public Rect GetBounds() =>
        Window.GetWindow(this) is PanelWindow window
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : Rect.Empty;

    private void DragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveHeaderElement(e.OriginalSource as DependencyObject) ||
            Window.GetWindow(this) is not PanelWindow panelWindow)
        {
            return;
        }

        // Double-clicking the bar rolls the panel up, matching the window-shade convention.
        if (e.ClickCount == 2)
        {
            ToggleShade();
            e.Handled = true;
            return;
        }

        Activated?.Invoke(this, EventArgs.Empty);
        _dragging = true;
        _dragStart = PointToScreen(e.GetPosition(this));
        _startX = panelWindow.Left;
        _startY = panelWindow.Top;
        DragSurface.CaptureMouse();
        DragSurface.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private static bool IsInteractiveHeaderElement(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or Slider or TextBoxBase)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void DragSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || Window.GetWindow(this) is not PanelWindow panelWindow)
        {
            return;
        }

        var current = PointToScreen(e.GetPosition(this));
        var x = _startX + current.X - _dragStart.X;
        var y = _startY + current.Y - _dragStart.Y;

        // Alt is the escape hatch: hold it and the panel goes exactly where the pointer is.
        if (Snap.IsFreeMove)
        {
            panelWindow.Left = x;
            panelWindow.Top = y;
            return;
        }

        var width = panelWindow.ActualWidth;
        var height = panelWindow.ActualHeight;

        // Guides come from the monitor the title bar is currently over, plus every sibling.
        var workArea = MonitorHelper.WorkAreaForPoint(new Point(x + width / 2, y + 14), panelWindow);
        var others = SnapRectsProvider?.Invoke(this) ?? [];
        panelWindow.Left = Snap.Span(x, width, Snap.VerticalLines(workArea, others));
        panelWindow.Top = Snap.Span(y, height, Snap.HorizontalLines(workArea, others));
    }

    private void DragSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        DragSurface.ReleaseMouseCapture();
        DragSurface.Cursor = Cursors.Arrow;
        CaptureDefinition();
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Called by <see cref="PanelWindow"/> after a window-chrome resize settles.</summary>
    public void NotifyGeometryChanged()
    {
        if (!_initialized)
        {
            return;
        }

        CaptureDefinition();
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    // ============================ Title ============================

    private void TitleText_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        e.Handled = true;
        TitleEditBox.Text = TitleText.Text;
        TitleEditBox.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    private void TitleEditBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitTitle();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelTitleEdit();
                e.Handled = true;
                break;
        }
    }

    private void TitleEditBox_OnLostFocus(object sender, RoutedEventArgs e) => CommitTitle();

    private void CommitTitle()
    {
        if (TitleEditBox.Visibility != Visibility.Visible)
        {
            return;
        }

        var name = TitleEditBox.Text.Trim();
        if (name.Length > 0 && name != Definition.Title)
        {
            Definition.Title = name;
            Definition.HasCustomTitle = true;
            TitleText.Text = name;
            SyncWindowTitle();
            PanelChanged?.Invoke(this, EventArgs.Empty);
        }

        CancelTitleEdit();
    }

    private void CancelTitleEdit()
    {
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
    }

    // ============================ Content controls ============================

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized)
        {
            return;
        }

        Definition.Opacity = e.NewValue;
        ApplyEffectiveOpacity();
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The dock-wide see-through slider multiplies the per-panel value instead of overwriting it,
    /// so panel-level tuning survives a global fade.
    /// </summary>
    public void SetGlobalOpacityFactor(double factor)
    {
        _globalOpacityFactor = Math.Clamp(factor, 0.35, 1);
        ApplyEffectiveOpacity();
    }

    private void ApplyEffectiveOpacity()
    {
        if (Window.GetWindow(this) is PanelWindow panelWindow)
        {
            panelWindow.Opacity = Math.Clamp(OpacitySlider.Value * _globalOpacityFactor, MinEffectiveOpacity, 1);
        }
    }

    private void NotesBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateNotesPlaceholder();
        if (!_initialized)
        {
            return;
        }

        Definition.Notes = NotesBox.Text;
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNotesPlaceholder() =>
        NotesPlaceholder.Visibility = NotesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddressBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Navigate(AddressBox.Text);
                Browser.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                SyncAddressFromBrowser();
                Browser.Focus();
                e.Handled = true;
                break;
        }
    }

    private void AddressBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_suppressAddressUpdate)
        {
            AddressBox.SelectAll();
        }
    }

    private void GoButton_OnClick(object sender, RoutedEventArgs e) => Navigate(AddressBox.Text);

    private void BackButton_OnClick(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.GoBack();

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.GoForward();

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e) => Reload();

    private void OpenExternalButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(AddressBox.Text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Keeps the OS window title in step with the panel's own title.</summary>
    private void SyncWindowTitle()
    {
        if (Window.GetWindow(this) is PanelWindow panelWindow)
        {
            panelWindow.Title = $"DriftDeck - {Definition.Title}";
        }
    }

    private void ShadeButton_OnClick(object sender, RoutedEventArgs e) => ToggleShade();

    private void ToggleShade()
    {
        if (Window.GetWindow(this) is not PanelWindow panelWindow)
        {
            return;
        }

        panelWindow.SetShaded(!panelWindow.IsShaded);
        PanelChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hides everything below the title bar while the panel is rolled up. The rows are
    /// collapsed rather than merely hidden so the window can actually reach 30px tall.
    /// </summary>
    public void SetShaded(bool shaded)
    {
        ContentRow.Height = shaded ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ToolbarRow.Height = shaded || Definition.Kind != PanelKind.Browser
            ? new GridLength(0)
            : GridLength.Auto;
        BrowserToolbar.Visibility = shaded || Definition.Kind != PanelKind.Browser
            ? Visibility.Collapsed
            : Visibility.Visible;
        ContentArea.Visibility = shaded ? Visibility.Collapsed : Visibility.Visible;
        ResizeHint.Visibility = shaded ? Visibility.Collapsed : Visibility.Visible;
        ShadeButton.Content = shaded ? "\uE70D" : "\uE70E";
        ShadeButton.ToolTip = shaded ? "Roll back down (Ctrl+M)" : "Roll up to the title bar (Ctrl+M)";
        OuterBorder.CornerRadius = new CornerRadius(shaded ? 6 : 7);
    }

    private void ZoomOutButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyContentScale(Definition.ContentScale - 0.1, true);

    private void ZoomInButton_OnClick(object sender, RoutedEventArgs e) =>
        ApplyContentScale(Definition.ContentScale + 0.1, true);

    private void ContentScaleText_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        ApplyContentScale(1, true);

    private void ApplyContentScale(double scale, bool notify)
    {
        scale = Math.Round(Math.Clamp(scale, 0.5, 1.5), 1);
        Definition.ContentScale = scale;
        ContentScaleText.Text = $"{scale * 100:0}%";
        // Off-default is worth noticing, but accent is reserved for state the user can act on.
        ContentScaleText.Foreground = (Brush)FindResource(
            Math.Abs(scale - 1) < 0.01 ? "MutedBrush" : "TextBrush");
        if (Browser.CoreWebView2 is not null)
        {
            Browser.ZoomFactor = scale;
        }

        var baseSize = (double)FindResource("TextMd");
        NotesBox.FontSize = baseSize * scale;
        NotesPlaceholder.FontSize = baseSize * scale;
        ChecklistItems.FontSize = baseSize * scale;
        ChecklistAddBox.FontSize = baseSize * scale;
        ApplyImageScale();
        // The readout is a display number rather than a step on the type scale; see the panel's
        // XAML for why it sits outside it.
        TimerReadout.FontSize = 44 * scale;
        if (notify)
        {
            PanelChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Active state is the one thing in the panel allowed to use the accent: the frame,
    /// the kind stripe, and a lifted title strip all move together.
    /// </summary>
    public void SetActive(bool active)
    {
        OuterBorder.BorderBrush = (Brush)FindResource(active ? "AccentBrush" : "StrokeSubtleBrush");
        KindStripe.Fill = (Brush)FindResource(active ? "AccentBrush" : "StrokeSubtleBrush");
        DragSurface.Background = (Brush)FindResource(active ? "SurfaceActiveBrush" : "SurfaceRaisedBrush");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTimerTicking();
        Browser.Dispose();
    }
}
