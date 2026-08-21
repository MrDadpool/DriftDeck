using System.Windows;
using System.Windows.Controls;
using DriftDeck.Models;

namespace DriftDeck;

/// <summary>
/// First-run tour. A transparent overlay whose only permanent chrome is a thin dock explains
/// itself to nobody, and the two global shortcuts are undiscoverable by clicking around, so the
/// first launch says them out loud once and then never again.
/// </summary>
public partial class OnboardingWindow : Window
{
    private const int StepCount = 3;

    private readonly StackPanel[] _steps;
    private int _step;

    /// <summary>Panels the user asked to be created once the tour closes.</summary>
    public bool CreateBrowserPanel { get; private set; }

    public bool CreateNotesPanel { get; private set; }

    public bool EnableAutoSwitch { get; private set; }

    public OnboardingWindow(AppSettings settings)
    {
        InitializeComponent();
        _steps = [StepOne, StepTwo, StepThree];
        InteractionHotkeyText.Text = settings.InteractionHotkey;
        VisibilityHotkeyText.Text = settings.VisibilityHotkey;
        AutoSwitchCheckBox.IsChecked = settings.AutoSwitchLayouts;
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        _step = Math.Clamp(index, 0, StepCount - 1);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
        }

        StepLabel.Text = $"STEP {_step + 1} OF {StepCount}";
        TitleText.Text = _step switch
        {
            0 => "DriftDeck is a workspace that floats over anything",
            1 => "Two shortcuts do the work",
            _ => "Set up your first panels"
        };

        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == StepCount - 1 ? "Start using DriftDeck" : "Next";
        SkipButton.Visibility = _step == StepCount - 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_step < StepCount - 1)
        {
            ShowStep(_step + 1);
            return;
        }

        CreateBrowserPanel = CreateBrowserCheckBox.IsChecked == true;
        CreateNotesPanel = CreateNotesCheckBox.IsChecked == true;
        EnableAutoSwitch = AutoSwitchCheckBox.IsChecked == true;
        DialogResult = true;
    }

    /// <summary>
    /// Skipping still counts as completing the tour. Re-showing it every launch would punish the
    /// user for dismissing it, and Settings covers everything it says.
    /// </summary>
    private void SkipButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
