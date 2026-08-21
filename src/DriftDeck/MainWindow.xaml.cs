using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DriftDeck.Controls;
using DriftDeck.Models;
using DriftDeck.Services;

namespace DriftDeck;

public partial class MainWindow : Window
{
    private const double DockHeight = 68;
    private const double DockMinWidth = 960;
    private const double CollapsedWidth = 250;
    private const double CollapsedHeight = 18;

    private readonly LayoutStore _layoutStore = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _deleteArmTimer;
    private readonly List<PanelHost> _panelHosts = [];
    private readonly List<PanelWindow> _panelWindows = [];
    private readonly Stack<PanelDefinition> _closedPanels = new();
    private readonly ForegroundWatcher _foreground = new();
    private readonly DispatcherTimer _autoSwitchTimer;

    private OverlayLayout _layout = OverlayLayout.CreateDefault();
    private AppSettings _settings;
    private GlobalHotkeyService? _hotkeys;
    private OverlayWindowService? _overlayWindow;
    private TrayIconService? _tray;
    private HwndSource? _activationSource;
    private nint _windowHandle;
    private bool _clickThrough;
    private bool _initializing = true;
    private bool _dockCollapsed;
    private bool _deleteArmed;
    private bool _shuttingDown;
    private PanelHost? _activeHost;
    private double _expandedDockLeft;
    private double _expandedDockTop;
    private double _expandedDockWidth = 1020;

    /// <summary>Rule waiting out the settle delay before its layout is loaded.</summary>
    private LayoutRule? _pendingRule;

    /// <summary>
    /// Process the user last loaded a layout for by hand. Automatic switching stays out of the
    /// way until they move to a different application, so a deliberate choice is never undone
    /// a second later by a rule.
    /// </summary>
    private string _manualOverrideProcess = string.Empty;

    public ICommand AddBrowserCommand { get; }
    public ICommand AddNotesCommand { get; }
    public ICommand ReopenPanelCommand { get; }
    public ICommand SaveLayoutCommand { get; }

    public MainWindow()
    {
        _settings = _settingsStore.Load();
        InitializeComponent();

        AddBrowserCommand = new RelayCommand(() => AddPanel(PanelKind.Browser));
        AddNotesCommand = new RelayCommand(() => AddPanel(PanelKind.Notes));
        ReopenPanelCommand = new RelayCommand(ReopenLastClosedPanel, () => _closedPanels.Count > 0);
        SaveLayoutCommand = new RelayCommand(() => _ = SaveNamedLayoutAsync());

        // Registered here rather than in XAML: InputBindings sit outside the visual tree,
        // so RelativeSource bindings to the window never resolve.
        InputBindings.Add(new KeyBinding(AddBrowserCommand, Key.B, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(AddNotesCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ReopenPanelCommand, Key.T, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(SaveLayoutCommand, Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => CyclePanels(1)), Key.Tab, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => CyclePanels(-1)), Key.Tab,
            ModifierKeys.Control | ModifierKeys.Shift));

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _saveTimer.Tick += SaveTimer_OnTick;
        // A short settle delay, so alt-tabbing through applications does not load three layouts.
        _autoSwitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _autoSwitchTimer.Tick += AutoSwitchTimer_OnTick;
        _foreground.Changed += Foreground_OnChanged;

        _deleteArmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _deleteArmTimer.Tick += (_, _) => DisarmDelete();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        // The dock is topmost like the panels are, so clicking it has to lift it past them.
        Activated += (_, _) => WindowOrder.BringToFront(_windowHandle);
        LocationChanged += OnWindowLayoutChanged;
        SizeChanged += OnWindowLayoutChanged;
        Closing += OnClosing;
    }

    // ============================ Startup ============================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        _activationSource = HwndSource.FromHwnd(_windowHandle);
        _activationSource?.AddHook(ActivationWindowProcedure);
        _overlayWindow = new OverlayWindowService(_windowHandle);
        CreateTrayIcon();

        try
        {
            _hotkeys = CreateHotkeyService(_settings);
        }
        catch (Exception exception)
        {
            _settings = new AppSettings();
            try
            {
                _hotkeys = CreateHotkeyService(_settings);
                SetStatus($"Saved shortcuts were invalid, so defaults were restored. {exception.Message}", StatusKind.Warning);
            }
            catch (Exception fallbackException)
            {
                SetStatus($"Global shortcuts are unavailable: {fallbackException.Message}", StatusKind.Warning);
            }
        }
    }

    private void CreateTrayIcon()
    {
        _tray = new TrayIconService();
        _tray.ToggleVisibilityRequested += (_, _) => ToggleVisibility();
        _tray.TogglePassThroughRequested += (_, _) => ToggleInteraction();
        _tray.SettingsRequested += (_, _) =>
        {
            SetOverlayVisible(true);
            OpenSettings();
        };
        _tray.QuitRequested += (_, _) => Close();
        _tray.UpdateState(true, false);
    }

    private nint ActivationWindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message != App.ShowOverlayMessage)
        {
            return nint.Zero;
        }

        handled = true;
        WindowState = WindowState.Normal;
        SetOverlayVisible(true);
        Activate();
        return nint.Zero;
    }

    private GlobalHotkeyService CreateHotkeyService(AppSettings settings)
    {
        if (!HotkeyGesture.TryParse(settings.InteractionHotkey, out var interactionGesture, out var interactionError))
        {
            throw new FormatException(interactionError);
        }

        if (!HotkeyGesture.TryParse(settings.VisibilityHotkey, out var visibilityGesture, out var visibilityError))
        {
            throw new FormatException(visibilityError);
        }

        var service = new GlobalHotkeyService(_windowHandle, interactionGesture, visibilityGesture);
        service.InteractionToggleRequested += (_, _) => ToggleInteraction();
        service.VisibilityToggleRequested += (_, _) => ToggleVisibility();
        return service;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _layout = await _layoutStore.LoadLastAsync();
        ApplyWindowLayout(_layout);
        RefreshLayoutNames();
        LayoutComboBox.Text = _layout.Name;
        LoadPanelHosts();
        _initializing = false;
        SetStatus(IdleStatus(), StatusKind.Info);

        if (!_settings.HasSeenOnboarding)
        {
            await RunOnboardingAsync();
        }

        ApplyAutoSwitchSetting();
        ReportPreviousCrash();
        _ = CheckForUpdatesAsync();

        if (_settings.StartHidden)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                SetOverlayVisible(false);
                _tray?.ShowHint("DriftDeck is running",
                    $"The overlay started hidden. Press {_settings.VisibilityHotkey} or double-click this icon to show it.");
            }, DispatcherPriority.ContextIdle);
        }
    }

    /// <summary>
    /// First launch only. The dock alone cannot explain pass-through or the global shortcuts,
    /// and both are invisible until someone says they exist.
    /// </summary>
    private async Task RunOnboardingAsync()
    {
        var tour = new OnboardingWindow(_settings) { Owner = this };
        var completed = tour.ShowDialog() == true;

        _settings.HasSeenOnboarding = true;
        if (completed)
        {
            _settings.AutoSwitchLayouts = tour.EnableAutoSwitch;
            if (tour.CreateBrowserPanel)
            {
                AddPanel(PanelKind.Browser);
            }

            if (tour.CreateNotesPanel)
            {
                AddPanel(PanelKind.Notes);
            }
        }

        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (IOException)
        {
            // The tour would simply run once more. Not worth an error message on first launch.
        }
    }

    /// <summary>
    /// Says out loud that the last run was killed rather than closed. An overlay usually dies
    /// with the game it was sitting over, and silently carrying on hides that a crash log exists.
    /// </summary>
    private void ReportPreviousCrash()
    {
        var sentinel = App.Sentinel;
        if (sentinel is null || !sentinel.PreviousRunCrashed)
        {
            return;
        }

        var when = sentinel.PreviousRunEndedAt is { } endedAt
            ? $" from {endedAt:t}"
            : string.Empty;
        SetStatus($"The previous session ended unexpectedly. Your layout{when} was restored. " +
                  "Settings can open the crash log folder.", StatusKind.Warning);
    }

    /// <summary>
    /// One anonymous request to the public release list, and only ever a notice: DriftDeck is a
    /// portable folder with no installer, so it must not replace itself behind the user's back.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (!_settings.CheckForUpdates)
        {
            return;
        }

        using var updates = new UpdateService();
        var update = await updates.CheckAsync();
        if (update is null || update.Tag == _settings.DismissedUpdateTag)
        {
            return;
        }

        _settings.DismissedUpdateTag = update.Tag;
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (IOException)
        {
            // Worst case the same version is announced again next launch.
        }

        SetStatus($"DriftDeck {update.Tag} is available. Settings has the download link.", StatusKind.Info);
        _tray?.ShowHint("DriftDeck update available",
            $"{update.Tag} has been published. Open Settings to download it.");
    }

    // ============================ Per-application layouts ============================

    /// <summary>Starts or stops foreground watching to match the current settings.</summary>
    private void ApplyAutoSwitchSetting()
    {
        var wanted = _settings.AutoSwitchLayouts && _settings.LayoutRules.Count > 0;
        if (wanted == _foreground.IsRunning)
        {
            return;
        }

        if (wanted)
        {
            _foreground.Start();
        }
        else
        {
            _foreground.Stop();
            _autoSwitchTimer.Stop();
            _pendingRule = null;
        }
    }

    private void Foreground_OnChanged(object? sender, ForegroundApp app)
    {
        // Moving to a different application clears the hold a manual load put on switching.
        if (!app.ProcessName.Equals(_manualOverrideProcess, StringComparison.OrdinalIgnoreCase))
        {
            _manualOverrideProcess = string.Empty;
        }

        var rule = LayoutRule.FirstMatch(_settings.LayoutRules, app.ProcessName, app.WindowTitle);
        if (rule is null
            || !string.IsNullOrEmpty(_manualOverrideProcess)
            || LayoutStore.NormalizeName(rule.LayoutName).Equals(_layout.Name, StringComparison.OrdinalIgnoreCase))
        {
            _autoSwitchTimer.Stop();
            _pendingRule = null;
            return;
        }

        _pendingRule = rule;
        _autoSwitchTimer.Stop();
        _autoSwitchTimer.Start();
    }

    private async void AutoSwitchTimer_OnTick(object? sender, EventArgs e)
    {
        _autoSwitchTimer.Stop();
        var rule = _pendingRule;
        _pendingRule = null;
        if (rule is null)
        {
            return;
        }

        // Confirm the application is still in front, so a layout never loads for a window the
        // user has already tabbed away from.
        var app = _foreground.Current();
        if (app.IsEmpty || !rule.Matches(app.ProcessName, app.WindowTitle))
        {
            return;
        }

        var target = LayoutStore.NormalizeName(rule.LayoutName);
        if (target.Equals(_layout.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await SwitchLayoutAsync(target);
        SetStatus($"Loaded '{_layout.Name}' for {app.ProcessName} - {_panelHosts.Count} panels", StatusKind.Success);
    }

    private string IdleStatus() =>
        $"{_settings.InteractionHotkey} pass-through  ·  {_settings.VisibilityHotkey} hide  ·  " +
        $"Ctrl+B web  ·  Ctrl+N notes  ·  {_panelHosts.Count} panel{(_panelHosts.Count == 1 ? "" : "s")}";

    private void ApplyWindowLayout(OverlayLayout layout)
    {
        Width = Math.Max(DockMinWidth, layout.Width);
        Height = DockHeight;
        Left = layout.Left;
        Top = layout.Top;
        Opacity = 1;
        ClampDockIntoView();
        OverlayTransparencySlider.Value = Math.Round((1 - Math.Clamp(layout.Opacity, 0.35, 1)) * 100);
        TransparencyValueText.Text = $"{OverlayTransparencySlider.Value:0}%";
    }

    private void ClampDockIntoView()
    {
        var workArea = MonitorHelper.WorkAreaForPoint(new Point(Left + 40, Top + 13), this);
        if (workArea.IsEmpty)
        {
            return;
        }

        Width = Math.Min(Width, Math.Max(DockMinWidth, workArea.Width));
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - DockHeight));
    }

    // ============================ Panels ============================

    private void LoadPanelHosts()
    {
        foreach (var existingHost in _panelHosts)
        {
            existingHost.Dispose();
        }

        foreach (var panelWindow in _panelWindows)
        {
            panelWindow.Content = null;
            panelWindow.Close();
        }

        _panelHosts.Clear();
        _panelWindows.Clear();
        foreach (var definition in _layout.Panels)
        {
            AddPanelHost(definition, activate: false);
        }

        if (_panelHosts.Count > 0)
        {
            ActivatePanel(_panelHosts[^1]);
        }
    }

    private void AddPanelHost(PanelDefinition definition, bool activate)
    {
        var host = new PanelHost(definition)
        {
            SnapRectsProvider = GetSnapRects
        };
        host.CloseRequested += PanelHost_OnCloseRequested;
        host.PanelChanged += (_, _) => ScheduleSave();
        host.Activated += (_, _) => ActivatePanel(host);

        var panelWindow = new PanelWindow(definition, host)
        {
            SnapRectsProvider = GetSnapRectsForWindow
        };
        panelWindow.UserActivated += (_, _) => ActivatePanel(host);
        _panelHosts.Add(host);
        _panelWindows.Add(panelWindow);

        panelWindow.ShowPanel(EffectiveOpacityFor(definition));
        host.SetGlobalOpacityFactor(GlobalOpacityFactor);
        panelWindow.SetClickThrough(_clickThrough);
        if (activate)
        {
            ActivatePanel(host);
        }
        else
        {
            host.SetActive(false);
        }
    }

    private double GlobalOpacityFactor => Math.Clamp(1 - (OverlayTransparencySlider.Value / 100), 0.35, 1);

    private double EffectiveOpacityFor(PanelDefinition definition) =>
        Math.Clamp(Math.Clamp(definition.Opacity, 0.35, 1) * GlobalOpacityFactor, 0.2, 1);

    /// <summary>Edges a dragged panel can snap to: every other panel plus the dock itself.</summary>
    private IReadOnlyList<Rect> GetSnapRects(PanelHost moving) =>
        CollectSnapRects(host => ReferenceEquals(host, moving));

    /// <summary>Same guides, asked for by the window while it is being resized.</summary>
    private IReadOnlyList<Rect> GetSnapRectsForWindow(PanelWindow moving) =>
        CollectSnapRects(host => ReferenceEquals(host, moving.Host));

    private List<Rect> CollectSnapRects(Func<PanelHost, bool> exclude)
    {
        var rects = new List<Rect>();
        foreach (var host in _panelHosts)
        {
            if (exclude(host))
            {
                continue;
            }

            var bounds = host.GetBounds();
            if (!bounds.IsEmpty)
            {
                rects.Add(bounds);
            }
        }

        if (!_dockCollapsed)
        {
            rects.Add(new Rect(Left, Top, ActualWidth, ActualHeight));
        }

        return rects;
    }

    /// <summary>
    /// Marks a panel active and lifts it within the always-on-top band. Reordering goes
    /// through SetWindowPos rather than a Topmost toggle, so there is no flash and the
    /// foreground application underneath keeps its focus.
    /// </summary>
    private void ActivatePanel(PanelHost activeHost)
    {
        _activeHost = activeHost;
        foreach (var host in _panelHosts)
        {
            host.SetActive(ReferenceEquals(host, activeHost));
        }

        _panelWindows.FirstOrDefault(window => ReferenceEquals(window.Host, activeHost))?.BringToFront();
    }

    /// <summary>Cycles focus and z-order through the open panels, most recent first.</summary>
    private void CyclePanels(int direction)
    {
        if (_panelHosts.Count == 0)
        {
            SetStatus("No panels are open · Ctrl+B for web, Ctrl+N for notes", StatusKind.Info);
            return;
        }

        var current = _activeHost is null ? -1 : _panelHosts.IndexOf(_activeHost);
        var next = ((current + direction) % _panelHosts.Count + _panelHosts.Count) % _panelHosts.Count;
        var host = _panelHosts[next];
        ActivatePanel(host);

        var window = _panelWindows.FirstOrDefault(candidate => ReferenceEquals(candidate.Host, host));
        if (window is not null && !_clickThrough)
        {
            window.Activate();
        }

        SetStatus($"Focused ‘{host.Definition.Title}’ · {next + 1} of {_panelHosts.Count}", StatusKind.Info);
    }

    private void PanelHost_OnCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not PanelHost host)
        {
            return;
        }

        host.CaptureDefinition();
        _closedPanels.Push(host.Definition);
        ReopenButton.IsEnabled = true;

        host.Dispose();
        var panelWindow = _panelWindows.FirstOrDefault(window => ReferenceEquals(window.Host, host));
        if (panelWindow is not null)
        {
            panelWindow.Content = null;
            panelWindow.Close();
            _panelWindows.Remove(panelWindow);
        }

        _panelHosts.Remove(host);
        if (ReferenceEquals(_activeHost, host))
        {
            _activeHost = null;
        }

        _layout.Panels.RemoveAll(panel => panel.Id == host.Definition.Id);
        ScheduleSave();
        SetStatus($"Closed ‘{host.Definition.Title}’ · Ctrl+Shift+T to bring it back", StatusKind.Info);
    }

    private void ReopenLastClosedPanel()
    {
        if (_closedPanels.Count == 0)
        {
            SetStatus("No recently closed panel to reopen", StatusKind.Warning);
            return;
        }

        var definition = _closedPanels.Pop();
        definition.Id = Guid.NewGuid();
        _layout.Panels.Add(definition);
        AddPanelHost(definition, activate: true);
        ReopenButton.IsEnabled = _closedPanels.Count > 0;
        ScheduleSave();
        SetStatus($"Reopened ‘{definition.Title}’", StatusKind.Success);
    }

    private void AddPanel(PanelKind kind)
    {
        // Cascade below the dock so a new panel never lands exactly on top of the last one.
        var offset = (_panelHosts.Count % 6) * 28;
        var x = Left + offset;
        var y = Top + ActualHeight + 16 + offset;
        var definition = kind == PanelKind.Browser
            ? PanelDefinition.CreateBrowser(x, y)
            : PanelDefinition.CreateNotes(x, y);

        _layout.Panels.Add(definition);
        AddPanelHost(definition, activate: true);
        ScheduleSave();
        SetStatus($"{definition.Title} panel added · drag its title bar to move it", StatusKind.Success);
    }

    // ============================ Modes ============================

    private void ToggleInteraction()
    {
        if (!IsVisible)
        {
            SetOverlayVisible(true);
        }

        _clickThrough = !_clickThrough;
        _overlayWindow?.SetClickThrough(_clickThrough);
        foreach (var panelWindow in _panelWindows)
        {
            panelWindow.SetClickThrough(_clickThrough);
        }

        ModeText.Text = _clickThrough ? "Pass-through" : "Interactive";
        ModePill.Background = (Brush)FindResource(_clickThrough ? "WarnPillBrush" : "AccentPillBrush");
        ModeText.Foreground = (Brush)FindResource(_clickThrough ? "WarnBrush" : "AccentBrush");
        PassThroughButton.Content = _clickThrough ? "Interact" : "Pass through";

        // A dimmed, warm-bordered dock is the only affordance left once clicks stop landing.
        Motion.Hold(PassThroughScrim, OpacityProperty, _clickThrough ? 1 : 0, Motion.Fast);

        SetStatus(_clickThrough
                ? $"Pass-through is ON · clicks go to the app underneath · press {_settings.InteractionHotkey} to interact again"
                : $"Interactive · press {_settings.InteractionHotkey} to pass clicks through",
            _clickThrough ? StatusKind.Warning : StatusKind.Success);

        _tray?.UpdateState(IsVisible, _clickThrough);
        if (_clickThrough)
        {
            _tray?.ShowHint("Pass-through is on",
                $"DriftDeck no longer takes clicks. Press {_settings.InteractionHotkey} to interact with it again.");
        }
    }

    private void ToggleVisibility() => SetOverlayVisible(!IsVisible);

    private void SetOverlayVisible(bool visible)
    {
        if (!visible)
        {
            foreach (var panelWindow in _panelWindows)
            {
                panelWindow.Hide();
            }

            Hide();
            _tray?.UpdateState(false, _clickThrough);
            return;
        }

        Show();
        foreach (var panelWindow in _panelWindows)
        {
            panelWindow.ShowPanel(EffectiveOpacityFor(panelWindow.Definition));
            panelWindow.SetClickThrough(_clickThrough);
        }

        if (!_clickThrough)
        {
            Activate();
        }

        _tray?.UpdateState(true, _clickThrough);
    }

    // ============================ Status ============================

    private enum StatusKind
    {
        Info,
        Success,
        Warning
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusText.Text = message;
        StatusDot.Fill = kind switch
        {
            StatusKind.Success => (Brush)FindResource("AccentBrush"),
            StatusKind.Warning => (Brush)FindResource("WarnBrush"),
            _ => (Brush)FindResource("MutedBrush")
        };

        // Short fade so a repeated message still reads as a new event.
        Motion.To(StatusPanel, OpacityProperty, 0.25, 1, Motion.Base);
    }

    // ============================ Persistence ============================

    private void ScheduleSave()
    {
        if (_initializing || _shuttingDown)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async void SaveTimer_OnTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        await SaveCurrentLayoutAsync(false);
    }

    private async Task SaveCurrentLayoutAsync(bool showStatus)
    {
        CaptureLayout();
        try
        {
            await _layoutStore.SaveAsync(_layout);
            RefreshLayoutNames();
            if (showStatus)
            {
                SetStatus($"Saved layout ‘{_layout.Name}’ · {_panelHosts.Count} panels", StatusKind.Success);
            }
        }
        catch (IOException exception)
        {
            SetStatus($"Layout could not be saved: {exception.Message}", StatusKind.Warning);
        }
    }

    private void CaptureLayout()
    {
        if (WindowState == WindowState.Normal && !_dockCollapsed)
        {
            _layout.Left = Left;
            _layout.Top = Top;
            _layout.Width = ActualWidth;
            _layout.Height = DockHeight;
        }

        _layout.Opacity = GlobalOpacityFactor;
        foreach (var host in _panelHosts)
        {
            host.CaptureDefinition();
        }
    }

    private void RefreshLayoutNames()
    {
        var currentText = LayoutComboBox.Text;
        LayoutComboBox.ItemsSource = _layoutStore.ListNames();
        LayoutComboBox.Text = string.IsNullOrWhiteSpace(currentText) ? _layout.Name : currentText;
    }

    private async void LoadLayoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var requested = LayoutStore.NormalizeName(LayoutComboBox.Text);
        if (requested.Equals(_layout.Name, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"‘{requested}’ is already loaded", StatusKind.Info);
            return;
        }

        // Loading by hand outranks the rules: automatic switching is paused for this
        // application until the user moves to a different one.
        _manualOverrideProcess = _foreground.Current().ProcessName;
        _autoSwitchTimer.Stop();
        _pendingRule = null;

        await SwitchLayoutAsync(requested);
        SetStatus($"Loaded ‘{_layout.Name}’ · {_panelHosts.Count} panels", StatusKind.Success);
    }

    /// <summary>
    /// Replaces the live panels with a stored layout, saving the current one first. Shared by the
    /// Load button and by rule-driven switching so both paths behave identically.
    /// </summary>
    private async Task SwitchLayoutAsync(string name)
    {
        await SaveCurrentLayoutAsync(false);
        _initializing = true;
        _layout = await _layoutStore.LoadAsync(name);
        ApplyWindowLayout(_layout);
        LayoutComboBox.Text = _layout.Name;
        LoadPanelHosts();
        _closedPanels.Clear();
        ReopenButton.IsEnabled = false;
        _initializing = false;

        // Records which layout is now current. Without this the "last layout" pointer still
        // names the one we just left, and the next launch would restore the wrong workspace.
        await SaveCurrentLayoutAsync(false);
    }

    private void SaveLayoutButton_OnClick(object sender, RoutedEventArgs e) => _ = SaveNamedLayoutAsync();

    private async Task SaveNamedLayoutAsync()
    {
        var name = LayoutStore.NormalizeName(LayoutComboBox.Text);
        var renaming = !name.Equals(_layout.Name, StringComparison.OrdinalIgnoreCase);
        _layout.Name = name;
        LayoutComboBox.Text = name;
        await SaveCurrentLayoutAsync(false);
        SetStatus(renaming
                ? $"Saved as new layout ‘{name}’"
                : $"Saved layout ‘{name}’ · {_panelHosts.Count} panels",
            StatusKind.Success);
    }

    private async void CopyLayoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveCurrentLayoutAsync(false);
        var existingNames = _layoutStore.ListNames();
        var baseName = $"{_layout.Name} Copy";
        var copyName = baseName;
        var suffix = 2;
        while (existingNames.Contains(copyName, StringComparer.OrdinalIgnoreCase))
        {
            copyName = $"{baseName} {suffix++}";
        }

        try
        {
            // Deliberately does not switch layouts: duplicating should leave you where you were.
            await _layoutStore.SaveCopyAsync(_layout, copyName);
            RefreshLayoutNames();
            LayoutComboBox.Text = _layout.Name;
            SetStatus($"Copied to ‘{copyName}’ · still editing ‘{_layout.Name}’", StatusKind.Success);
        }
        catch (IOException exception)
        {
            SetStatus($"Copy failed: {exception.Message}", StatusKind.Warning);
        }
    }

    /// <summary>
    /// Two-step delete instead of a modal dialog: a message box can pull focus out of a
    /// fullscreen game, which is exactly what an overlay must not do.
    /// </summary>
    private async void DeleteLayoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = LayoutStore.NormalizeName(LayoutComboBox.Text);
        if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("The Default layout cannot be deleted", StatusKind.Warning);
            return;
        }

        if (!_layoutStore.Exists(name))
        {
            SetStatus($"There is no saved layout named ‘{name}’", StatusKind.Warning);
            return;
        }

        if (!_deleteArmed)
        {
            _deleteArmed = true;
            _deleteArmTimer.Stop();
            _deleteArmTimer.Start();
            DeleteButton.Content = "Confirm";
            DeleteButton.BorderBrush = (Brush)FindResource("DangerBrush");
            SetStatus($"Click Confirm to delete ‘{name}’ · cancels in 4 seconds", StatusKind.Warning);
            return;
        }

        DisarmDelete();
        if (!_layoutStore.Delete(name))
        {
            SetStatus($"‘{name}’ could not be deleted", StatusKind.Warning);
            return;
        }

        if (_layout.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            _initializing = true;
            _layout = await _layoutStore.LoadAsync("Default");
            ApplyWindowLayout(_layout);
            LoadPanelHosts();
            _initializing = false;
        }

        RefreshLayoutNames();
        LayoutComboBox.Text = _layout.Name;
        SetStatus($"Deleted layout ‘{name}’", StatusKind.Success);
    }

    private void DisarmDelete()
    {
        _deleteArmTimer.Stop();
        if (!_deleteArmed)
        {
            return;
        }

        _deleteArmed = false;
        DeleteButton.Content = "Delete";
        DeleteButton.ClearValue(BorderBrushProperty);
    }

    // ============================ Settings ============================

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => OpenSettings();

    private async void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings, _layoutStore.ListNames(),
            App.Sentinel?.LogDirectory ?? string.Empty)
        {
            Owner = IsVisible ? this : null
        };
        if (dialog.ShowDialog() != true || dialog.ResultSettings is null)
        {
            return;
        }

        var previousSettings = _settings;
        _hotkeys?.Dispose();
        try
        {
            _hotkeys = CreateHotkeyService(dialog.ResultSettings);
            _settings = dialog.ResultSettings;
            ApplyAutoSwitchSetting();
            await _settingsStore.SaveAsync(_settings);
            SetStatus($"Settings saved · {_settings.InteractionHotkey} pass-through · {_settings.VisibilityHotkey} hide",
                StatusKind.Success);
        }
        catch (Exception exception)
        {
            _hotkeys?.Dispose();
            _settings = previousSettings;
            ApplyAutoSwitchSetting();
            try
            {
                _hotkeys = CreateHotkeyService(previousSettings);
            }
            catch
            {
                _hotkeys = null;
            }

            SetStatus($"Windows rejected those shortcuts, so the previous ones were kept: {exception.Message}",
                StatusKind.Warning);
        }
    }

    // ============================ Toolbar handlers ============================

    private void AddBrowserButton_OnClick(object sender, RoutedEventArgs e) => AddPanel(PanelKind.Browser);

    private void AddNotesButton_OnClick(object sender, RoutedEventArgs e) => AddPanel(PanelKind.Notes);

    private void ReopenPanelButton_OnClick(object sender, RoutedEventArgs e) => ReopenLastClosedPanel();

    private void InteractionButton_OnClick(object sender, RoutedEventArgs e) => ToggleInteraction();

    private void OverlayTransparencySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var factor = Math.Clamp(1 - (e.NewValue / 100), 0.35, 1);
        _layout.Opacity = factor;
        foreach (var host in _panelHosts)
        {
            host.SetGlobalOpacityFactor(factor);
        }

        if (TransparencyValueText is not null)
        {
            TransparencyValueText.Text = $"{e.NewValue:0}%";
        }

        if (!_initializing)
        {
            SetStatus($"See-through {e.NewValue:0}% · per-panel sliders still apply on top", StatusKind.Info);
            ScheduleSave();
        }
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e)
    {
        if (!_dockCollapsed)
        {
            ScheduleSave();
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MinimizeButton_OnClick(sender, e);
            return;
        }

        DragMove();
    }

    // ============================ Collapse ============================

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_dockCollapsed)
        {
            RestoreDock();
        }
        else
        {
            CollapseDock();
        }
    }

    private void CollapseDock()
    {
        DisarmDelete();
        _expandedDockLeft = Left;
        _expandedDockTop = Top;
        _expandedDockWidth = ActualWidth;
        _dockCollapsed = true;

        FadeDock(() =>
        {
            DockToolbar.Visibility = Visibility.Collapsed;
            StatusRow.Height = new GridLength(0);
            CollapseButton.Content = "\uE922"; // restore glyph
            CollapseButton.ToolTip = "Restore the dock";
            MinWidth = CollapsedWidth;
            MaxHeight = CollapsedHeight;
            Width = CollapsedWidth;
            Height = CollapsedHeight;

            var workArea = MonitorHelper.WorkAreaForWindow(this);
            Left = workArea.Right - Width - 12;
            Top = workArea.Bottom - Height - 8;
        });
    }

    private void RestoreDock()
    {
        _dockCollapsed = false;
        FadeDock(() =>
        {
            MinWidth = DockMinWidth;
            MaxHeight = DockHeight;
            Width = Math.Max(DockMinWidth, _expandedDockWidth);
            Height = DockHeight;
            Left = _expandedDockLeft;
            Top = _expandedDockTop;
            DockToolbar.Visibility = Visibility.Visible;
            StatusRow.Height = GridLength.Auto;
            CollapseButton.Content = "\uE921"; // collapse glyph
            CollapseButton.ToolTip = "Collapse dock to the bottom-right of this monitor";
            ClampDockIntoView();
            Activate();
            SetStatus(IdleStatus(), StatusKind.Info);
        });
    }

    /// <summary>
    /// Cross-fades the dock around a geometry change. Animating window width directly
    /// stutters on multi-monitor setups, so the resize happens inside the dark frame.
    /// </summary>
    private void FadeDock(Action change) => Motion.CrossFade(DockRoot, change);

    // ============================ Shutdown ============================

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _shuttingDown = true;
        _saveTimer.Stop();
        _deleteArmTimer.Stop();
        _autoSwitchTimer.Stop();
        _foreground.Dispose();
        await SaveCurrentLayoutAsync(false);

        foreach (var host in _panelHosts)
        {
            host.Dispose();
        }

        foreach (var panelWindow in _panelWindows)
        {
            panelWindow.Content = null;
            panelWindow.Close();
        }

        _tray?.Dispose();
        _hotkeys?.Dispose();
        _overlayWindow?.Dispose();
        _activationSource?.RemoveHook(ActivationWindowProcedure);
    }
}

