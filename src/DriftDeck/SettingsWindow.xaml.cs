using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DriftDeck.Models;
using DriftDeck.Services;

namespace DriftDeck;

public partial class SettingsWindow : Window
{
    private static readonly AppSettings Defaults = new();

    private readonly AppSettings _original;
    private readonly ObservableCollection<LayoutRule> _rules = [];

    /// <summary>
    /// Observable so an import can make the new layouts pickable at once. A plain list bound
    /// one-way raises nothing when it is replaced, and the rule rows would keep offering the
    /// old names until Settings was closed and reopened.
    /// </summary>
    private readonly ObservableCollection<string> _layoutNames = [];
    private readonly ObservableCollection<QuickLayout> _quickLayouts = [];
    private readonly ForegroundWatcher _foreground = new();
    private readonly LayoutStore _layoutStore = new();
    private readonly string _logDirectory;
    private DispatcherTimer? _captureTimer;
    private int _captureSecondsLeft;

    public AppSettings? ResultSettings { get; private set; }

    /// <summary>
    /// True once an import has written layouts. Import touches disk immediately rather than on
    /// Save, so the caller has to refresh its layout list even if the dialog is then cancelled.
    /// </summary>
    public bool LayoutsChanged { get; private set; }

    /// <summary>Layout names offered by every rule row's picker.</summary>
    public IReadOnlyList<string> LayoutNames => _layoutNames;

    /// <summary>Digits a quick-layout row may claim.</summary>
    public IReadOnlyList<int> SlotOptions { get; } =
        Enumerable.Range(QuickLayout.MinSlot, QuickLayout.MaxSlot - QuickLayout.MinSlot + 1).ToList();

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> layoutNames, string logDirectory)
    {
        _original = settings;
        _logDirectory = logDirectory;
        foreach (var name in layoutNames)
        {
            _layoutNames.Add(name);
        }

        InitializeComponent();

        InteractionHotkeyBox.Text = settings.InteractionHotkey;
        VisibilityHotkeyBox.Text = settings.VisibilityHotkey;
        StartHiddenCheckBox.IsChecked = settings.StartHidden;
        AutoSwitchCheckBox.IsChecked = settings.AutoSwitchLayouts;
        CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
        IdleDimCheckBox.IsChecked = settings.IdleDimEnabled;
        IdleDimSecondsBox.Text = settings.ClampedIdleDimSeconds.ToString();
        IdleDimPercentBox.Text = Math.Clamp(settings.IdleDimPercent,
            AppSettings.MinIdleDimPercent, AppSettings.MaxIdleDimPercent).ToString();
        FullscreenWarningCheckBox.IsChecked = settings.WarnOnExclusiveFullscreen;
        SuspendHiddenCheckBox.IsChecked = settings.SuspendHiddenPanels;

        // The registry is the truth here, not a copy in settings.json: the user can turn this
        // off from Task Manager, and a stored duplicate would then disagree with reality.
        RunAtLoginCheckBox.IsChecked = StartupRegistration.IsEnabled;
        UpdateStatusText.Text = $"Version {UpdateService.CurrentVersion.ToString(3)}";

        // Edited copies, so cancelling leaves the running configuration untouched.
        foreach (var rule in settings.LayoutRules)
        {
            _rules.Add(new LayoutRule
            {
                Enabled = rule.Enabled,
                ProcessName = rule.ProcessName,
                TitleContains = rule.TitleContains,
                LayoutName = rule.LayoutName
            });
        }

        foreach (var quick in settings.QuickLayouts)
        {
            _quickLayouts.Add(new QuickLayout { Slot = quick.Slot, LayoutName = quick.LayoutName });
        }

        RulesList.ItemsSource = _rules;
        QuickLayoutsList.ItemsSource = _quickLayouts;
        _rules.CollectionChanged += (_, _) => UpdateRulesPlaceholder();
        _quickLayouts.CollectionChanged += (_, _) => UpdateQuickLayoutsPlaceholder();
        UpdateRulesPlaceholder();
        UpdateQuickLayoutsPlaceholder();
        Closed += (_, _) => CleanUp();
    }

    private void UpdateRulesPlaceholder() =>
        NoRulesText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateQuickLayoutsPlaceholder()
    {
        NoQuickLayoutsText.Visibility = _quickLayouts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddQuickLayoutButton.IsEnabled = _quickLayouts.Count < QuickLayout.MaxSlot;
    }

    // ============================ Quick layouts ============================

    private void AddQuickLayoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var slot = QuickLayout.NextFreeSlot(_quickLayouts);
        if (slot is null)
        {
            ValidationText.Text = "All nine digits are already assigned.";
            return;
        }

        ValidationText.Text = string.Empty;
        _quickLayouts.Add(new QuickLayout
        {
            Slot = slot.Value,
            LayoutName = LayoutNames.FirstOrDefault() ?? "Default"
        });
    }

    private void RemoveQuickLayoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QuickLayout quick })
        {
            _quickLayouts.Remove(quick);
        }
    }

    // ============================ Shortcuts ============================

    /// <summary>
    /// Records the pressed combination directly instead of asking the user to spell a
    /// gesture out as text.
    /// </summary>
    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Tab)
        {
            e.Handled = false;
            return;
        }

        if (key is Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        // Modifier-only presses are the user still assembling the gesture.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        var candidate = new HotkeyGesture(Keyboard.Modifiers, key).ToString();
        if (!HotkeyGesture.TryParse(candidate, out var gesture, out var error))
        {
            ValidationText.Text = error;
            return;
        }

        ValidationText.Text = string.Empty;
        box.Text = gesture.ToString();
    }

    private void HotkeyBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HintText.Text = "Listening. Press the shortcut you want, or Esc to stop.";
        if (sender is TextBox box)
        {
            box.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }
    }

    private void HotkeyBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HintText.Text = "Click a shortcut box and press the keys. Windows-reserved combinations are refused.";
        if (sender is TextBox box)
        {
            box.ClearValue(BorderBrushProperty);
        }
    }

    private void ResetHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target })
        {
            return;
        }

        ValidationText.Text = string.Empty;
        if (target == "Interaction")
        {
            InteractionHotkeyBox.Text = Defaults.InteractionHotkey;
        }
        else
        {
            VisibilityHotkeyBox.Text = Defaults.VisibilityHotkey;
        }
    }

    // ============================ Rules ============================

    private void AddRuleButton_OnClick(object sender, RoutedEventArgs e) =>
        _rules.Add(new LayoutRule { LayoutName = LayoutNames.FirstOrDefault() ?? "Default" });

    private void RemoveRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LayoutRule rule })
        {
            _rules.Remove(rule);
        }
    }

    /// <summary>
    /// Typing an executable name means knowing it. Instead the user clicks the application they
    /// want, and DriftDeck reads whatever is in front once the countdown ends.
    /// </summary>
    private void CaptureRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_captureTimer is not null)
        {
            StopCapture("Capture cancelled.");
            return;
        }

        _captureSecondsLeft = 4;
        CaptureRuleButton.Content = "Cancel capture";
        CaptureStatusText.Text = $"Click the application you want. Capturing in {_captureSecondsLeft}s.";

        _captureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _captureTimer.Tick += (_, _) =>
        {
            _captureSecondsLeft--;
            if (_captureSecondsLeft > 0)
            {
                CaptureStatusText.Text = $"Click the application you want. Capturing in {_captureSecondsLeft}s.";
                return;
            }

            var app = _foreground.Current();
            if (app.IsEmpty)
            {
                StopCapture("Nothing to capture — DriftDeck was still in front.");
                return;
            }

            _rules.Add(new LayoutRule
            {
                ProcessName = app.ProcessName,
                LayoutName = LayoutNames.FirstOrDefault() ?? "Default"
            });
            StopCapture($"Captured ‘{app.ProcessName}’. Pick the layout it should load.");
            Activate();
        };
        _captureTimer.Start();
    }

    private void StopCapture(string message)
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        CaptureRuleButton.Content = "Capture the next app I switch to";
        CaptureStatusText.Text = message;
    }

    // ============================ Updates and logs ============================

    private async void CheckNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";
        try
        {
            using var updates = new UpdateService();
            var update = await updates.CheckAsync();
            UpdateStatusText.Text = update is null
                ? $"Version {UpdateService.CurrentVersion.ToString(3)} is the latest release."
                : $"{update.Tag} is available. Use ‘Open releases’ to download it.";
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void OpenReleasesButton_OnClick(object sender, RoutedEventArgs e) =>
        UpdateService.OpenReleasePage(UpdateService.ReleasesPageUrl);

    private void OpenLogsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            Process.Start(new ProcessStartInfo(_logDirectory) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or System.ComponentModel.Win32Exception)
        {
            UpdateStatusText.Text = "The log folder could not be opened.";
        }
    }

    // ============================ Layout files ============================

    private async void ExportLayoutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export DriftDeck layouts",
            FileName = $"DriftDeck layouts{LayoutBundle.FileExtension}",
            DefaultExt = LayoutBundle.FileExtension,
            Filter = $"DriftDeck layouts (*{LayoutBundle.FileExtension})|*{LayoutBundle.FileExtension}",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportLayoutsButton.IsEnabled = false;
        try
        {
            var count = await LayoutBundleStore.ExportAsync(_layoutStore, dialog.FileName);
            LayoutFileStatusText.Text = count == 0
                ? "There were no saved layouts to export."
                : $"Exported {count} layout{(count == 1 ? "" : "s")}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LayoutFileStatusText.Text = $"Export failed: {exception.Message}";
        }
        finally
        {
            ExportLayoutsButton.IsEnabled = true;
        }
    }

    private async void ImportLayoutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import DriftDeck layouts",
            DefaultExt = LayoutBundle.FileExtension,
            Filter = $"DriftDeck layouts (*{LayoutBundle.FileExtension})|*{LayoutBundle.FileExtension}" +
                     "|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImportLayoutsButton.IsEnabled = false;
        try
        {
            var result = await LayoutBundleStore.ImportAsync(_layoutStore, dialog.FileName);
            if (result.Failed)
            {
                LayoutFileStatusText.Text = result.Error;
                return;
            }

            LayoutsChanged |= result.Added > 0;
            LayoutFileStatusText.Text = result.Renamed == 0
                ? $"Imported {result.Added} layout{(result.Added == 1 ? "" : "s")}."
                : $"Imported {result.Added}, renaming {result.Renamed} that already existed.";

            // Rule rows pick from this list, so a freshly imported layout has to become
            // selectable without closing and reopening Settings.
            _layoutNames.Clear();
            foreach (var name in _layoutStore.ListNames())
            {
                _layoutNames.Add(name);
            }
        }
        finally
        {
            ImportLayoutsButton.IsEnabled = true;
        }
    }

    // ============================ Commit ============================

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(InteractionHotkeyBox.Text, out var interactionGesture, out var interactionError))
        {
            ValidationText.Text = $"Pass-through shortcut: {interactionError}";
            return;
        }

        if (!HotkeyGesture.TryParse(VisibilityHotkeyBox.Text, out var visibilityGesture, out var visibilityError))
        {
            ValidationText.Text = $"Hide shortcut: {visibilityError}";
            return;
        }

        if (interactionGesture == visibilityGesture)
        {
            ValidationText.Text = "The two shortcuts must be different.";
            return;
        }

        // A rule with no application name can never match, and would read as working. Say so
        // rather than saving a row that quietly does nothing.
        if (_rules.Any(rule => string.IsNullOrWhiteSpace(rule.ProcessName)))
        {
            ValidationText.Text = "Every layout rule needs an application name. Remove the empty row or fill it in.";
            return;
        }

        // Two rows on one digit means the second registration is refused and the row reads as
        // working. Say so rather than saving a shortcut that does nothing.
        var duplicateSlot = _quickLayouts
            .GroupBy(quick => quick.Slot)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSlot is not null)
        {
            ValidationText.Text = $"Ctrl+Alt+{duplicateSlot.Key} is assigned twice. Give each digit one layout.";
            return;
        }

        if (_quickLayouts.Any(quick => string.IsNullOrWhiteSpace(quick.LayoutName)))
        {
            ValidationText.Text = "Every quick layout shortcut needs a layout. Remove the empty row or fill it in.";
            return;
        }

        // Applied before the dialog closes so the checkbox always reflects what was written,
        // and reported inline because a refused registry write must not read as success.
        var startupError = StartupRegistration.Apply(RunAtLoginCheckBox.IsChecked == true);
        if (startupError is not null)
        {
            ValidationText.Text = $"Start with Windows could not be changed: {startupError}";
            return;
        }

        ResultSettings = new AppSettings
        {
            InteractionHotkey = interactionGesture.ToString(),
            VisibilityHotkey = visibilityGesture.ToString(),
            StartHidden = StartHiddenCheckBox.IsChecked == true,
            HasSeenOnboarding = _original.HasSeenOnboarding,
            DismissedUpdateTag = _original.DismissedUpdateTag,
            AutoSwitchLayouts = AutoSwitchCheckBox.IsChecked == true,
            CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
            IdleDimEnabled = IdleDimCheckBox.IsChecked == true,
            IdleDimSeconds = ParseBounded(IdleDimSecondsBox.Text, Defaults.IdleDimSeconds,
                AppSettings.MinIdleDimSeconds, AppSettings.MaxIdleDimSeconds),
            IdleDimPercent = ParseBounded(IdleDimPercentBox.Text, Defaults.IdleDimPercent,
                AppSettings.MinIdleDimPercent, AppSettings.MaxIdleDimPercent),
            WarnOnExclusiveFullscreen = FullscreenWarningCheckBox.IsChecked == true,
            SuspendHiddenPanels = SuspendHiddenCheckBox.IsChecked == true,
            QuickLayouts = _quickLayouts
                .Where(quick => quick.IsValidSlot)
                .Select(quick => new QuickLayout
                {
                    Slot = quick.Slot,
                    LayoutName = LayoutStore.NormalizeName(quick.LayoutName)
                })
                .OrderBy(quick => quick.Slot)
                .ToList(),
            LayoutRules = _rules
                .Select(rule => new LayoutRule
                {
                    Enabled = rule.Enabled,
                    ProcessName = rule.ProcessName.Trim(),
                    TitleContains = rule.TitleContains.Trim(),
                    LayoutName = LayoutStore.NormalizeName(rule.LayoutName)
                })
                .ToList()
        };
        DialogResult = true;
    }

    /// <summary>
    /// Out-of-range or unreadable input falls back to the default rather than refusing to save.
    /// These two boxes are comfort settings; a typo in one should not block a shortcut change.
    /// </summary>
    private static int ParseBounded(string text, int fallback, int minimum, int maximum) =>
        int.TryParse(text.Trim(), out var value) ? Math.Clamp(value, minimum, maximum) : fallback;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CleanUp()
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        _foreground.Dispose();
    }
}
