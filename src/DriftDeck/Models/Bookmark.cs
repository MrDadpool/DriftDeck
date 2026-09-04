namespace DriftDeck.Models;

/// <summary>
/// A page the user deliberately kept. Bookmarks are global rather than per layout: a saved
/// address is worth reaching from any workspace, whereas the history of what was opened
/// belongs to the workspace it happened in.
/// </summary>
public sealed class Bookmark
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>What the picker shows. Falls back to the address when a page had no title.</summary>
    public string Label => string.IsNullOrWhiteSpace(Title) ? Url : Title;
}
