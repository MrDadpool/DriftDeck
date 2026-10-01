using DriftDeck.Models;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// The checklist panel's rules — what counts as an item, what the footer says, what "clear
/// done" removes — live apart from the control so they can be checked without a window.
/// </summary>
public class ChecklistTests
{
    private static ChecklistItem Item(string text, bool done = false) =>
        new() { Text = text, IsDone = done };

    [Fact]
    public void TryCreate_trims_what_the_user_typed()
    {
        Assert.True(Checklist.TryCreate("  buy milk  ", out var item));
        Assert.Equal("buy milk", item.Text);
        Assert.False(item.IsDone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void TryCreate_refuses_blank_input(string? text)
    {
        // Enter on an empty add box has to be a no-op, not a row of nothing.
        Assert.False(Checklist.TryCreate(text, out _));
    }

    [Fact]
    public void TryCreate_cuts_overlong_text_rather_than_refusing_it()
    {
        // A paste should still land; it just cannot be allowed to drive the panel's layout.
        Assert.True(Checklist.TryCreate(new string('x', 500), out var item));
        Assert.Equal(Checklist.MaximumTextLength, item.Text.Length);
    }

    [Fact]
    public void TryCreate_keeps_text_that_is_exactly_at_the_cap()
    {
        var text = new string('x', Checklist.MaximumTextLength);
        Assert.True(Checklist.TryCreate(text, out var item));
        Assert.Equal(text, item.Text);
    }

    [Fact]
    public void RemainingCount_counts_only_unticked_items()
    {
        Assert.Equal(2, Checklist.RemainingCount([Item("a"), Item("b", done: true), Item("c")]));
    }

    [Fact]
    public void Summary_says_nothing_is_there_yet_for_an_empty_list()
    {
        Assert.Equal("No items yet", Checklist.Summary([]));
    }

    [Fact]
    public void Summary_reports_what_is_left_rather_than_what_is_done()
    {
        Assert.Equal("2 of 3 left", Checklist.Summary([Item("a"), Item("b", done: true), Item("c")]));
    }

    [Fact]
    public void Summary_calls_out_a_finished_list()
    {
        Assert.Equal("All 2 done", Checklist.Summary([Item("a", done: true), Item("b", done: true)]));
    }

    [Fact]
    public void ClearCompleted_removes_the_ticked_items_and_keeps_the_rest_in_order()
    {
        var items = new List<ChecklistItem>
        {
            Item("a"),
            Item("b", done: true),
            Item("c"),
            Item("d", done: true),
        };

        Assert.Equal(2, Checklist.ClearCompleted(items));
        Assert.Equal(["a", "c"], items.Select(item => item.Text));
    }

    [Fact]
    public void ClearCompleted_on_a_list_with_nothing_ticked_changes_nothing()
    {
        var items = new List<ChecklistItem> { Item("a"), Item("b") };

        Assert.Equal(0, Checklist.ClearCompleted(items));
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void ClearCompleted_can_empty_the_list()
    {
        var items = new List<ChecklistItem> { Item("a", done: true) };

        Assert.Equal(1, Checklist.ClearCompleted(items));
        Assert.Empty(items);
    }

    [Fact]
    public void An_item_raises_a_change_notification_when_it_is_ticked()
    {
        // The panel persists off these notifications, so a silent setter would lose the tick.
        var item = Item("a");
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.IsDone = true;
        item.Text = "b";

        Assert.Equal([nameof(ChecklistItem.IsDone), nameof(ChecklistItem.Text)], changed);
    }

    [Fact]
    public void An_item_stays_quiet_when_the_value_does_not_actually_change()
    {
        var item = Item("a");
        var changes = 0;
        item.PropertyChanged += (_, _) => changes++;

        item.Text = "a";
        item.IsDone = false;

        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_new_checklist_panel_starts_empty()
    {
        var panel = PanelDefinition.CreateChecklist(10, 20);

        Assert.Equal(PanelKind.Checklist, panel.Kind);
        Assert.Empty(panel.Items);
    }
}
