using System.Windows;

namespace DriftDeck.Services;

/// <summary>
/// Places every panel back onto one monitor.
/// <para>
/// Display recovery covers a monitor disappearing. This covers the other half: a panel dragged
/// somewhere the user cannot find it. An always-on-top window that is off-screen is worse than a
/// missing one, because it is still there, still topmost, and still cannot be reached — and the
/// only handle a panel offers is its own title bar, which is exactly the part that is gone.
/// </para>
/// <para>
/// Pure, like <see cref="Snap"/> and <see cref="Models.LayoutRule"/>, so the placement can be
/// reasoned about and tested without a window or a display.
/// </para>
/// </summary>
public static class Gather
{
    /// <summary>Gap left around the outside of the work area and between panels.</summary>
    public const double Margin = 12;

    /// <summary>
    /// Diagonal step between panels in the same column, matching the cascade a new panel gets.
    /// A cascade rather than a tile: overlapping title bars stay individually clickable, and
    /// tiling would have to resize panels, which gathering must not do.
    /// </summary>
    public const double Cascade = 28;

    /// <summary>
    /// Positions for <paramref name="sizes"/>, in the same order, all inside
    /// <paramref name="workArea"/>. <paramref name="reserveTop"/> keeps the dock's own strip
    /// clear so panels do not land under it.
    /// </summary>
    /// <remarks>
    /// A panel larger than the work area is clamped to its top-left corner rather than skipped:
    /// the point of the command is that every panel ends up somewhere reachable, and a panel too
    /// big to fit is still reachable by its title bar once its corner is on screen.
    /// </remarks>
    public static List<Point> Positions(Rect workArea, double reserveTop, IReadOnlyList<Size> sizes)
    {
        var results = new List<Point>(sizes.Count);
        var columnLeft = workArea.Left + Margin;
        var top = workArea.Top + reserveTop + Margin;
        var columnWidth = 0d;
        var indexInColumn = 0;

        foreach (var size in sizes)
        {
            var y = top + (indexInColumn * Cascade);

            // Start a new column once the cascade would push a panel past the bottom edge. The
            // first panel of a column is placed regardless, so an oversized panel cannot loop.
            if (indexInColumn > 0 && y + size.Height > workArea.Bottom - Margin)
            {
                columnLeft += columnWidth + Margin;
                top = workArea.Top + reserveTop + Margin;
                columnWidth = 0;
                indexInColumn = 0;
                y = top;
            }

            var x = columnLeft + (indexInColumn * Cascade);

            // Columns wrap around to the left edge rather than marching off the right of the
            // monitor, which would recreate the problem this command exists to fix.
            if (x + size.Width > workArea.Right - Margin)
            {
                columnLeft = workArea.Left + Margin;
                x = columnLeft;
            }

            results.Add(new Point(
                Snap.ToGrid(Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width))),
                Snap.ToGrid(Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)))));

            columnWidth = Math.Max(columnWidth, size.Width);
            indexInColumn++;
        }

        return results;
    }
}
