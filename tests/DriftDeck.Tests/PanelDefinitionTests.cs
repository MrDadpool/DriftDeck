using DriftDeck.Models;
using Xunit;

namespace DriftDeck.Tests;

public sealed class PanelDefinitionTests
{
    [Fact]
    public void A_duplicate_carries_checklist_timer_and_image_content()
    {
        var end = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var original = new PanelDefinition
        {
            Kind = PanelKind.Checklist,
            Items = [new ChecklistItem { Text = "Boss key", IsDone = true }],
            ImagePath = @"C:\maps\route.png",
            TimerDurationSeconds = 90,
            TimerRemainingSeconds = 42,
            TimerEndUtc = end
        };

        var copy = original.Clone();

        var item = Assert.Single(copy.Items);
        Assert.Equal("Boss key", item.Text);
        Assert.True(item.IsDone);
        Assert.Equal(@"C:\maps\route.png", copy.ImagePath);
        Assert.Equal(90, copy.TimerDurationSeconds);
        Assert.Equal(42, copy.TimerRemainingSeconds);
        Assert.Equal(end, copy.TimerEndUtc);
    }

    [Fact]
    public void A_duplicate_checklist_does_not_share_rows_with_the_original()
    {
        // Rows bind straight to the persisted items, so a shared instance would tick a box in
        // both panels at once.
        var original = new PanelDefinition { Items = [new ChecklistItem { Text = "Ore" }] };

        var copy = original.Clone();
        copy.Items[0].IsDone = true;

        Assert.NotSame(original.Items[0], copy.Items[0]);
        Assert.False(original.Items[0].IsDone);
    }
}
