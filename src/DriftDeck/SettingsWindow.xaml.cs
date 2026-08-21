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
    private readonly ForegroundWatcher _foreground = new();
    private readonly string _logDirectory;
    private DispatcherTimer? _captureTimer;
    private int _captureSecondsLeft;

    public AppSettings? ResultSettings { get; private set; }

    /// <summary>Layout names offered by every rule row's picker.</summary>
    public IReadOnlyList<string> LayoutNames { get; }

    public SettingsWindow(AppSettings settings, IReadOnlyList<string> layoutNames, string logDirectory)
    {
        _original = settings;
        LayoutNames = layoutNames;
        _logDirectory = logDirectory;
        InitializeComponent();

        InteractionHotkeyBox.Text = settings.InteractionHotkey;
        VisibilityHotkeyBox.Text = settings.VisibilityHotkey;
        StartHiddenCheckBox.IsChecked = settings.StartHidden;
        AutoSwitchCheckBox.IsChecked = settings.AutoSwitchLayouts;
        CheckForUpdatesCheckBox.IsChecked = settings.CheckForUpdates;
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

        RulesList.ItemsSource = _rules;
        _rules.CollectionChanged += (_, _) => UpdateRulesPlaceholder();
        UpdateRulesPlaceholder();
        Closed += (_, _) => CleanUp();
    }

    private void UpdateRulesPlaceholder() =>
        NoRulesText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

        ResultSettings = new AppSettings
        {
            InteractionHotkey = interactionGesture.ToString(),
            VisibilityHotkey = visibilityGesture.ToString(),
            StartHidden = StartHiddenCheckBox.IsChecked == true,
            HasSeenOnboarding = _original.HasSeenOnboarding,
            DismissedUpdateTag = _original.DismissedUpdateTag,
            AutoSwitchLayouts = AutoSwitchCheckBox.IsChecked == true,
            CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
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

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CleanUp()
    {
        _captureTimer?.Stop();
        _captureTimer = null;
        _foreground.Dispose();
    }
}
