namespace DriftDeck.Services;

/// <summary>
/// The recent-address list, as pure functions.
/// <para>
/// Typing an address into an 18-pixel toolbar during a game is the worst interaction left in
/// the product, and the addresses a user wants are nearly always ones they have already had
/// open. Kept small and deliberately dumb: newest first, no ranking, no scoring.
/// </para>
/// </summary>
public static class UrlHistory
{
    /// <summary>
    /// How many addresses a layout remembers. Short on purpose — the list has to be scannable
    /// at a glance inside a panel, and a long history is a worse answer than a bookmark.
    /// </summary>
    public const int Capacity = 12;

    /// <summary>
    /// Whether an address is worth remembering. Blank pages, error pages and non-web schemes
    /// are all things the user did not ask to go to.
    /// </summary>
    public static bool ShouldRecord(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// <paramref name="existing"/> with <paramref name="url"/> at the front, deduplicated and
    /// capped. Returns a new list rather than mutating: the caller owns a collection the UI is
    /// bound to, and rebuilding a twelve-item list is cheaper than reasoning about in-place
    /// reordering.
    /// </summary>
    public static List<string> Push(IEnumerable<string> existing, string url, int capacity = Capacity)
    {
        var result = new List<string> { url };
        foreach (var candidate in existing)
        {
            if (string.IsNullOrWhiteSpace(candidate) ||
                candidate.Equals(url, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(candidate);
            if (result.Count == capacity)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// A short form for the picker: host plus path, without the scheme or a trailing slash.
    /// The full address stays in the tool tip, so nothing is hidden — only shortened.
    /// </summary>
    public static string Shorten(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
        var path = (uri.PathAndQuery + uri.Fragment).TrimEnd('/');
        return path is "" or "/" ? host : host + path;
    }
}
