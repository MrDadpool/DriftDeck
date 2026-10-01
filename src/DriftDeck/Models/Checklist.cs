using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DriftDeck.Models;

/// <summary>
/// One line of a checklist panel. It raises change notifications because the panel binds
/// directly to the persisted objects rather than copying them into a view model — a checklist
/// is a list of two fields, and a parallel model would be more code than it saves.
/// </summary>
public sealed class ChecklistItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool IsDone
    {
        get => _isDone;
        set => Set(ref _isDone, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>
/// The rules a checklist panel follows, kept apart from the control so they can be reasoned
/// about and tested without a window.
/// </summary>
public static class Checklist
{
    /// <summary>
    /// Long enough for a real task, short enough that one line cannot push the panel's layout
    /// around. Text past the cap is cut rather than rejected, so a paste still lands.
    /// </summary>
    public const int MaximumTextLength = 200;

    /// <summary>
    /// Builds an item from what the user typed. Whitespace-only input is not an item, which is
    /// what makes Enter on an empty box a no-op rather than a row of nothing.
    /// </summary>
    public static bool TryCreate(string? text, out ChecklistItem item)
    {
        item = new ChecklistItem();
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        item.Text = trimmed.Length > MaximumTextLength ? trimmed[..MaximumTextLength] : trimmed;
        return true;
    }

    public static int RemainingCount(IEnumerable<ChecklistItem> items) =>
        items.Count(item => !item.IsDone);

    /// <summary>
    /// Progress for the panel footer. It reports what is left rather than what is done: a
    /// checklist exists to answer "what still needs doing".
    /// </summary>
    public static string Summary(IReadOnlyCollection<ChecklistItem> items)
    {
        if (items.Count == 0)
        {
            return "No items yet";
        }

        var remaining = RemainingCount(items);
        return remaining == 0
            ? $"All {items.Count} done"
            : $"{remaining} of {items.Count} left";
    }

    /// <summary>Drops the ticked items in place and reports how many went.</summary>
    public static int ClearCompleted(IList<ChecklistItem> items)
    {
        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index].IsDone)
            {
                items.RemoveAt(index);
                removed++;
            }
        }

        return removed;
    }
}
