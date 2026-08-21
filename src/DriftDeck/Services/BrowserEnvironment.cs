using System.IO;
using Microsoft.Web.WebView2.Core;

namespace DriftDeck.Services;

/// <summary>
/// One <see cref="CoreWebView2Environment"/> shared by every browser panel.
/// <para>
/// A bare <c>EnsureCoreWebView2Async()</c> per panel lets WebView2 pick its own defaults, which
/// means a separate browser process group per panel and a profile folder created next to the
/// executable — so a portable folder grows a <c>DriftDeck.exe.WebView2\</c> directory at runtime
/// and a sign-in in one panel is invisible to the next. Sharing one environment puts the profile
/// under <c>%LOCALAPPDATA%\DriftDeck\webview2</c>, lets panels share cookies and logins, and lets
/// WebView2 reuse a single browser process across panels instead of one per panel.
/// </para>
/// </summary>
public static class BrowserEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CoreWebView2Environment? _environment;

    /// <summary>Where the shared browser profile lives. Never written into layout JSON.</summary>
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DriftDeck",
        "webview2");

    /// <summary>
    /// Creates the environment on first use and hands the same instance to every later caller.
    /// Panels load concurrently, so creation is gated rather than raced.
    /// </summary>
    public static async Task<CoreWebView2Environment> GetAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await Gate.WaitAsync();
        try
        {
            if (_environment is null)
            {
                Directory.CreateDirectory(UserDataFolder);
                _environment = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
            }
        }
        finally
        {
            Gate.Release();
        }

        return _environment;
    }
}
