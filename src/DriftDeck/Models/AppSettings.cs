namespace DriftDeck.Models;

public sealed class AppSettings
{
    public string InteractionHotkey { get; set; } = "Ctrl+Alt+O";
    public string VisibilityHotkey { get; set; } = "Ctrl+Alt+H";
    public bool StartHidden { get; set; }
}
