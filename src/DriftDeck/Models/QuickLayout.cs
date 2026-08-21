namespace DriftDeck.Models;

/// <summary>
/// Binds <c>Ctrl+Alt+&lt;digit&gt;</c> to a named layout, so a layout can be loaded without
/// leaving the application underneath.
/// <para>
/// Per-application rules already cover switching that should happen by itself. This covers the
/// other half: deliberately wanting a different workspace right now, which otherwise means
/// clicking the dock — and clicking the dock means taking focus off a fullscreen game, which is
/// the one thing an overlay must avoid.
/// </para>
/// <para>
/// The slot is stored rather than inferred from list order. Deriving it from the sorted layout
/// names would silently remap every shortcut the moment a new layout was saved.
/// </para>
/// </summary>
public sealed class QuickLayout
{
    public const int MinSlot = 1;
    public const int MaxSlot = 9;

    /// <summary>Digit key, 1 to 9.</summary>
    public int Slot { get; set; } = MinSlot;

    public string LayoutName { get; set; } = "Default";

    /// <summary>Label for the settings row, matching what the user will actually press.</summary>
    public string Shortcut => $"Ctrl+Alt+{Slot}";

    public bool IsValidSlot => Slot is >= MinSlot and <= MaxSlot;

    /// <summary>
    /// Lowest digit not already spoken for, or null when all nine are taken. Used when adding a
    /// row, so the common case needs no thought from the user.
    /// </summary>
    public static int? NextFreeSlot(IEnumerable<QuickLayout> existing)
    {
        var taken = existing.Select(entry => entry.Slot).ToHashSet();
        for (var slot = MinSlot; slot <= MaxSlot; slot++)
        {
            if (taken.Add(slot))
            {
                return slot;
            }
        }

        return null;
    }
}
