using System.Windows;

namespace DriftDeck.Controls;

/// <summary>
/// Marks which buttons draw a theme's button artwork. Text buttons opt in through their style;
/// ghost and icon buttons opt out, because a framed button face on every title-bar glyph would
/// bury the panel content it sits beside. Without a theme the artwork brushes are transparent,
/// so the flag changes nothing.
/// </summary>
public static class ThemeArt
{
    public static readonly DependencyProperty ShowProperty = DependencyProperty.RegisterAttached(
        "Show", typeof(bool), typeof(ThemeArt), new FrameworkPropertyMetadata(false));

    public static bool GetShow(DependencyObject element) => (bool)element.GetValue(ShowProperty);

    public static void SetShow(DependencyObject element, bool value) => element.SetValue(ShowProperty, value);
}
