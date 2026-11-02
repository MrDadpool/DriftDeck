using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DriftDeck.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DriftDeck;

// A real child WebView preserves window.opener and window.close for provider sign-ins.
internal sealed class BrowserPopupWindow : Window
{
    private readonly WebView2CompositionControl _browser = new();
    private readonly TextBox _address = new() { IsReadOnly = true, Margin = new Thickness(8) };
    private OverlayWindowService? _overlay;
    private bool _closed;

    private BrowserPopupWindow(Window owner)
    {
        Owner = owner;
        Title = "Browser - DriftDeck";
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var area = MonitorHelper.WorkAreaForWindow(owner);
        Width = Math.Min(600, area.Width);
        Height = Math.Min(720, area.Height);
        var content = new DockPanel();
        DockPanel.SetDock(_address, Dock.Top);
        content.Children.Add(_address);
        content.Children.Add(_browser);
        Content = content;
        owner.IsVisibleChanged += OwnerVisibilityChanged;
        SourceInitialized += (_, _) => _overlay = new OverlayWindowService(new WindowInteropHelper(this).Handle);
        Closed += (_, _) =>
        {
            _closed = true;
            owner.IsVisibleChanged -= OwnerVisibilityChanged;
            _overlay?.Dispose();
            _browser.Dispose();
        };
    }

    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (Owner.IsVisible) Show();
        else Hide();
    }

    public static async Task OpenAsync(Window owner, CoreWebView2NewWindowRequestedEventArgs request)
    {
        using var deferral = request.GetDeferral();
        request.Handled = true;
        var popup = new BrowserPopupWindow(owner);
        try
        {
            popup.Show();
            await popup._browser.EnsureCoreWebView2Async(await BrowserEnvironment.GetAsync());
            if (popup._closed) return;
            var core = popup._browser.CoreWebView2;
            core.Profile.IsPasswordAutosaveEnabled = true;
            core.WindowCloseRequested += (_, _) => popup.Close();
            core.SourceChanged += (_, _) => popup._address.Text = core.Source;
            core.NewWindowRequested += async (_, nested) =>
            {
                try { await OpenAsync(popup, nested); }
                catch (Exception exception)
                {
                    App.Sentinel?.WriteCrashReport(exception, "Browser popup");
                }
            };
            // Assign before any navigation so WebView2 retains the opener relationship.
            request.NewWindow = core;
        }
        catch
        {
            if (!popup._closed) popup.Close();
            throw;
        }
        finally { deferral.Complete(); }
    }

    public static void SetClickThrough(Window owner, bool enabled)
    {
        foreach (var popup in owner.OwnedWindows.OfType<BrowserPopupWindow>())
        {
            popup._overlay?.SetClickThrough(enabled);
            SetClickThrough(popup, enabled);
        }
    }

    public static void CloseOwned(Window owner)
    {
        foreach (var popup in owner.OwnedWindows.OfType<BrowserPopupWindow>().ToArray()) popup.Close();
    }
}
