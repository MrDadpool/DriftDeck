using System.Windows;
using System.Windows.Input;

namespace DriftDeck.Services;

/// <summary>
/// Placement assistance shared by dragging and resizing.
/// <para>
/// Two levels, in priority order: an edge lands on a neighbouring edge when it is close to
/// one, and otherwise falls onto an 8-unit grid. The grid is what makes a screenful of panels
/// look deliberate without the user aiming for anything; the edge snap is what makes two
/// panels sit flush. Holding Alt turns both off, which is the standard escape hatch.
/// </para>
/// </summary>
public static class Snap
{
    /// <summary>Grid step, in the same family as the 4-unit spacing scale.</summary>
    public const double Grid = 8;

    /// <summary>How near an edge must be before it is pulled onto a neighbour.</summary>
    public const double Distance = 12;

    public static bool IsFreeMove => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

    /// <summary>
    /// Away-from-zero rounding rather than the default banker's rounding, so a value sitting
    /// exactly between two grid lines always resolves the same way instead of depending on
    /// which line happens to be even.
    /// </summary>
    public static double ToGrid(double value) =>
        Math.Round(value / Grid, MidpointRounding.AwayFromZero) * Grid;

    /// <summary>
    /// Places a span of <paramref name="length"/> starting at <paramref name="position"/>.
    /// Either end of the span may land on any of <paramref name="lines"/>.
    /// Callers check <see cref="IsFreeMove"/> first; this stays pure so it can be reasoned
    /// about and tested without a live input stack.
    /// </summary>
    public static double Span(double position, double length, IReadOnlyList<double> lines)
    {
        var best = double.NaN;
        var bestDistance = Distance;
        foreach (var line in lines)
        {
            foreach (var candidate in new[] { line, line - length })
            {
                var distance = Math.Abs(position - candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }

        return double.IsNaN(best) ? ToGrid(position) : best;
    }

    /// <summary>Places a single edge — used while resizing, where only one side moves.</summary>
    public static double Edge(double position, IReadOnlyList<double> lines)
    {
        var best = double.NaN;
        var bestDistance = Distance;
        foreach (var line in lines)
        {
            var distance = Math.Abs(position - line);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = line;
            }
        }

        return double.IsNaN(best) ? ToGrid(position) : best;
    }

    /// <summary>Vertical guide lines: the left and right edge of every rectangle.</summary>
    public static List<double> VerticalLines(Rect workArea, IReadOnlyList<Rect> others)
    {
        var lines = new List<double> { workArea.Left, workArea.Right };
        foreach (var rect in others)
        {
            lines.Add(rect.Left);
            lines.Add(rect.Right);
        }

        return lines;
    }

    /// <summary>Horizontal guide lines: the top and bottom edge of every rectangle.</summary>
    public static List<double> HorizontalLines(Rect workArea, IReadOnlyList<Rect> others)
    {
        var lines = new List<double> { workArea.Top, workArea.Bottom };
        foreach (var rect in others)
        {
            lines.Add(rect.Top);
            lines.Add(rect.Bottom);
        }

        return lines;
    }
}
