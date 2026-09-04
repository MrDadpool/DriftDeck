namespace DriftDeck.Controls;

/// <summary>
/// A message a panel wants shown in the dock's status strip. Panels have no status strip of
/// their own — one per panel would put the same sentence in six places — and they deliberately
/// know nothing about the dock, so they ask rather than write.
/// </summary>
public sealed class PanelStatusEventArgs(string message, bool isWarning) : EventArgs
{
    public string Message { get; } = message;

    /// <summary>True when the panel is reporting something that did not work.</summary>
    public bool IsWarning { get; } = isWarning;
}
