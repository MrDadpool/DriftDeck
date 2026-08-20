using System.Windows;
using System.Windows.Media.Animation;

namespace DriftDeck.Services;

/// <summary>
/// The code-side half of the motion token set declared in App.xaml.
/// Honours the Windows "show animations" accessibility setting: with animation off,
/// every call here lands on the end value immediately instead of easing into it.
/// An always-on-top overlay sits over whatever the user is actually watching, so
/// unwanted motion is worse here than in an ordinary window.
/// </summary>
public static class Motion
{
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(120));
    public static readonly Duration Base = new(TimeSpan.FromMilliseconds(200));
    public static readonly Duration Slow = new(TimeSpan.FromMilliseconds(320));

    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    public static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>
    /// Animates a double property to <paramref name="to"/>, leaving the local value in
    /// control afterwards (FillBehavior.Stop) so later assignments are not blocked by a
    /// lingering animation clock.
    /// </summary>
    public static void To(UIElement element, DependencyProperty property, double from, double to,
        Duration duration, Action? completed = null)
    {
        if (!Enabled)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            completed?.Invoke();
            return;
        }

        element.SetValue(property, to);
        var animation = new DoubleAnimation(from, to, duration)
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = EaseOut
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        element.BeginAnimation(property, animation);
    }

    /// <summary>Animates to a value, holding it after the animation ends.</summary>
    public static void Hold(UIElement element, DependencyProperty property, double to, Duration duration)
    {
        if (!Enabled)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            return;
        }

        element.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = EaseOut });
    }

    /// <summary>
    /// Fades an element out, runs <paramref name="change"/> while it is invisible, then fades
    /// back in. With animation off the change happens immediately with no flicker.
    /// </summary>
    public static void CrossFade(UIElement element, Action change)
    {
        if (!Enabled)
        {
            change();
            return;
        }

        var fadeOut = new DoubleAnimation(1, 0, Fast) { EasingFunction = EaseIn };
        fadeOut.Completed += (_, _) =>
        {
            change();
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Base) { FillBehavior = FillBehavior.Stop, EasingFunction = EaseOut });
        };
        element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private static T Freeze<T>(T easing) where T : Freezable
    {
        easing.Freeze();
        return easing;
    }
}
