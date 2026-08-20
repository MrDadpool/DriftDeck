namespace DriftDeck.Models;

public sealed class OverlayLayout
{
    public int Version { get; set; }
    public string Name { get; set; } = "Default";
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 980;
    public double Height { get; set; } = 94;
    public double Opacity { get; set; } = 0.98;
    public List<PanelDefinition> Panels { get; set; } = [];

    public static OverlayLayout CreateDefault() => new()
    {
        Version = 2,
        Panels =
        [
            PanelDefinition.CreateBrowser(80, 170),
            PanelDefinition.CreateNotes(670, 170)
        ]
    };
}

public sealed class PanelDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PanelKind Kind { get; set; }
    public string Title { get; set; } = "Browser";

    /// <summary>
    /// True once the user renames the panel, which stops the page title from overwriting it.
    /// </summary>
    public bool HasCustomTitle { get; set; }

    public string Url { get; set; } = "https://www.youtube.com";
    public string Notes { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 560;
    public double Height { get; set; } = 390;
    public double Opacity { get; set; } = 0.96;
    public double ContentScale { get; set; } = 1;

    /// <summary>Rolled up to its title bar. The panel keeps its place on screen.</summary>
    public bool IsCollapsed { get; set; }

    /// <summary>Height to return to when the panel is un-rolled.</summary>
    public double RestoreHeight { get; set; } = 390;

    public static PanelDefinition CreateBrowser(double x, double y) => new()
    {
        Kind = PanelKind.Browser,
        Title = "Browser",
        X = x,
        Y = y
    };

    public static PanelDefinition CreateNotes(double x, double y) => new()
    {
        Kind = PanelKind.Notes,
        Title = "Notes",
        X = x,
        Y = y,
        Width = 320,
        Height = 390
    };
}

public enum PanelKind
{
    Browser,
    Notes
}
