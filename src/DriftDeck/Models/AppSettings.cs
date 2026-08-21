namespace DriftDeck.Models;

public sealed class AppSettings
{
    public string InteractionHotkey { get; set; } = "Ctrl+Alt+O";
    public string VisibilityHotkey { get; set; } = "Ctrl+Alt+H";
    public bool StartHidden { get; set; }

    /// <summary>False until the first run finishes the welcome tour.</summary>
    public bool HasSeenOnboarding { get; set; }

    /// <summary>Switch layouts automatically when the foreground application changes.</summary>
    public bool AutoSwitchLayouts { get; set; }

    public List<LayoutRule> LayoutRules { get; set; } = [];

    /// <summary>Ask GitHub once per launch whether a newer release exists. No data is sent.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Release tag the user has already been told about, so one version nags once.</summary>
    public string DismissedUpdateTag { get; set; } = string.Empty;
}
