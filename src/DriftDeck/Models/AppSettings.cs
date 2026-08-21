using System.Text.Json.Serialization;

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

    /// <summary>
    /// Fade panels the user has not touched. Off by default: an overlay quietly changing its own
    /// opacity is startling if nobody asked for it.
    /// </summary>
    public bool IdleDimEnabled { get; set; }

    /// <summary>Seconds of no pointer, keyboard, or focus activity before a panel fades.</summary>
    public int IdleDimSeconds { get; set; } = 20;

    /// <summary>How far a dimmed panel fades, as a percentage of its normal opacity.</summary>
    public int IdleDimPercent { get; set; } = 50;

    /// <summary>Warn when Windows reports an exclusive-fullscreen application owning the screen.</summary>
    public bool WarnOnExclusiveFullscreen { get; set; } = true;

    /// <summary>
    /// Stop browser panels doing work while the overlay is hidden. On by default: a hidden panel
    /// that is still decoding video is spending the exact resources the user hid it to reclaim.
    /// A page that must keep a live connection open is the reason this can be turned off.
    /// </summary>
    public bool SuspendHiddenPanels { get; set; } = true;

    /// <summary>Layouts reachable by <c>Ctrl+Alt+&lt;digit&gt;</c> without touching the dock.</summary>
    public List<QuickLayout> QuickLayouts { get; set; } = [];

    public const int MinIdleDimSeconds = 3;
    public const int MaxIdleDimSeconds = 600;
    public const int MinIdleDimPercent = 10;
    public const int MaxIdleDimPercent = 95;

    // Derived, so they are kept out of settings.json rather than written back as stale copies.
    [JsonIgnore]
    public int ClampedIdleDimSeconds =>
        Math.Clamp(IdleDimSeconds, MinIdleDimSeconds, MaxIdleDimSeconds);

    [JsonIgnore]
    public double IdleDimFactor =>
        Math.Clamp(IdleDimPercent, MinIdleDimPercent, MaxIdleDimPercent) / 100d;
}
