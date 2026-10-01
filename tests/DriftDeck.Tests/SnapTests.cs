using System.Windows;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// Snap decides where a dragged or resized edge lands. The rules it encodes — edge beats grid,
/// nothing beyond 12 units counts, ties resolve away from zero — are invisible in the running
/// app, so they are exactly the kind of thing that silently rots.
/// </summary>
public class SnapTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(5, 8)]
    [InlineData(8, 8)]
    [InlineData(100, 104)]
    [InlineData(-5, -8)]
    public void ToGrid_rounds_to_the_nearest_eight(double value, double expected) =>
        Assert.Equal(expected, Snap.ToGrid(value));

    [Fact]
    public void ToGrid_resolves_an_exact_midpoint_away_from_zero()
    {
        // Banker's rounding would send 4 down to 0 and 12 up to 16, which makes an identical
        // gesture land differently depending on which grid line happens to be even.
        Assert.Equal(8, Snap.ToGrid(4));
        Assert.Equal(16, Snap.ToGrid(12));
        Assert.Equal(-8, Snap.ToGrid(-4));
    }

    [Fact]
    public void Edge_falls_back_to_the_grid_when_no_line_is_near()
    {
        Assert.Equal(104, Snap.Edge(100, [500, 900]));
    }

    [Fact]
    public void Edge_prefers_a_guide_line_over_the_grid()
    {
        // 103 is 1 unit from the guide and 5 from the nearest grid line.
        Assert.Equal(103, Snap.Edge(102, [103]));
    }

    [Fact]
    public void Edge_ignores_a_line_at_exactly_the_snap_distance()
    {
        // The threshold is strict, so 12 units away is outside it and the grid wins.
        Assert.Equal(112, Snap.Edge(112, [100]));
    }

    [Fact]
    public void Edge_picks_the_closest_of_several_lines()
    {
        Assert.Equal(104, Snap.Edge(105, [100, 104, 110]));
    }

    [Fact]
    public void Span_can_land_its_leading_edge_on_a_line()
    {
        Assert.Equal(200, Snap.Span(198, 50, [200]));
    }

    [Fact]
    public void Span_can_land_its_trailing_edge_on_a_line()
    {
        // A panel 50 wide whose right edge should meet 200 must start at 150.
        Assert.Equal(150, Snap.Span(152, 50, [200]));
    }

    [Fact]
    public void Span_falls_back_to_the_grid_when_neither_edge_is_near()
    {
        Assert.Equal(400, Snap.Span(402, 50, [900]));
    }

    [Fact]
    public void VerticalLines_covers_the_work_area_and_every_rectangle()
    {
        var lines = Snap.VerticalLines(
            new Rect(0, 0, 1920, 1080),
            [new Rect(100, 50, 300, 200)]);

        Assert.Equal([0, 1920, 100, 400], lines);
    }

    [Fact]
    public void HorizontalLines_covers_the_work_area_and_every_rectangle()
    {
        var lines = Snap.HorizontalLines(
            new Rect(0, 0, 1920, 1080),
            [new Rect(100, 50, 300, 200)]);

        Assert.Equal([0, 1080, 50, 250], lines);
    }

    [Fact]
    public void Guide_lines_with_no_other_windows_are_just_the_work_area()
    {
        var workArea = new Rect(10, 20, 100, 100);
        Assert.Equal([10, 110], Snap.VerticalLines(workArea, []));
        Assert.Equal([20, 120], Snap.HorizontalLines(workArea, []));
    }
}
