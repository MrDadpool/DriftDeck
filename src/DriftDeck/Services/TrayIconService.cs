using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace DriftDeck.Services;

/// <summary>
/// Tray presence so a hidden overlay is still reachable without remembering a hotkey.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _visibilityItem;
    private readonly Forms.ToolStripMenuItem _passThroughItem;
    private bool _disposed;

    public event EventHandler? ToggleVisibilityRequested;
    public event EventHandler? TogglePassThroughRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    public TrayIconService()
    {
        _visibilityItem = new Forms.ToolStripMenuItem("Hide overlay");
        _visibilityItem.Click += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);

        _passThroughItem = new Forms.ToolStripMenuItem("Pass-through mode");
        _passThroughItem.Click += (_, _) => TogglePassThroughRequested?.Invoke(this, EventArgs.Empty);

        var settingsItem = new Forms.ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var quitItem = new Forms.ToolStripMenuItem("Quit DriftDeck");
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([
            _visibilityItem,
            _passThroughItem,
            new Forms.ToolStripSeparator(),
            settingsItem,
            new Forms.ToolStripSeparator(),
            quitItem
        ]);

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DriftDeck",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ToggleVisibilityRequested?.Invoke(this, EventArgs.Empty);
    }

    private static Icon LoadIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                return Icon.ExtractAssociatedIcon(path) ?? SystemIcons.Application;
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            // Fall through to the system icon; a missing tray glyph must never stop startup.
        }

        return SystemIcons.Application;
    }

    public void UpdateState(bool overlayVisible, bool passThrough)
    {
        _visibilityItem.Text = overlayVisible ? "Hide overlay" : "Show overlay";
        _passThroughItem.Checked = passThrough;
        _icon.Text = passThrough ? "DriftDeck - pass-through" : "DriftDeck";
    }

    public void ShowHint(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
