using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DriftDeck.Models;
using DriftDeck.Services;

namespace DriftDeck;

public partial class SettingsWindow : Window
{
    private static readonly AppSettings Defaults = new();

    public AppSettings? ResultSettings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        InteractionHotkeyBox.Text = settings.InteractionHotkey;
        VisibilityHotkeyBox.Text = settings.VisibilityHotkey;
        StartHiddenCheckBox.IsChecked = settings.StartHidden;
    }

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

        ResultSettings = new AppSettings
        {
            InteractionHotkey = interactionGesture.ToString(),
            VisibilityHotkey = visibilityGesture.ToString(),
            StartHidden = StartHiddenCheckBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
