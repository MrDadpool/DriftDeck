namespace DriftDeck.Models;

/// <summary>
/// Maps a foreground application to a layout. Matching is read-only: DriftDeck compares the
/// process name and window title that Windows already publishes for every top-level window.
/// Nothing is read out of the target process.
/// </summary>
public sealed class LayoutRule
{
    public bool Enabled { get; set; } = true;

    /// <summary>Executable name without the extension, e.g. <c>eldenring</c>. Case-insensitive.</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Optional substring of the window title. Lets one launcher executable drive several
    /// layouts. Empty means the process name alone decides the match.
    /// </summary>
    public string TitleContains { get; set; } = string.Empty;

    public string LayoutName { get; set; } = "Default";

    public bool Matches(string processName, string windowTitle)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(ProcessName))
        {
            return false;
        }

        if (!ProcessName.Trim().Equals(processName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(TitleContains)
               || windowTitle.Contains(TitleContains.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Title-qualified rules win over bare process rules, so a specific mode of an application
    /// beats the catch-all entry for the same executable regardless of list order.
    /// </summary>
    public static LayoutRule? FirstMatch(IEnumerable<LayoutRule> rules, string processName, string windowTitle)
    {
        LayoutRule? processOnlyMatch = null;
        foreach (var rule in rules)
        {
            if (!rule.Matches(processName, windowTitle))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rule.TitleContains))
            {
                return rule;
            }

            processOnlyMatch ??= rule;
        }

        return processOnlyMatch;
    }
}
