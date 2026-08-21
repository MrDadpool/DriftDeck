using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace DriftDeck.Services;

/// <summary>A newer published release than the one running.</summary>
public sealed record UpdateInfo(string Tag, Version Version, string ReleaseUrl);

/// <summary>
/// Asks GitHub once per launch whether a newer release exists.
/// <para>
/// The check is a plain anonymous GET of the public releases endpoint. Nothing about the user,
/// the machine, or the applications DriftDeck is running over is sent, and the request carries
/// no identifier beyond the User-Agent GitHub requires. DriftDeck ships as a portable folder
/// with no installer, so an update is never applied silently: the user is told, and the release
/// page opens in their browser if they ask for it.
/// </para>
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Repository = "MrDadpool/DriftDeck";
    public const string ReleasesPageUrl = $"https://github.com/{Repository}/releases/latest";

    private static readonly Uri LatestReleaseApi =
        new($"https://api.github.com/repos/{Repository}/releases/latest");

    private readonly HttpClient _client;
    private bool _disposed;

    public UpdateService()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"DriftDeck/{CurrentVersion}");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>The running build, taken from the assembly so the csproj stays the single source.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Returns the newer release, or null when the build is current, the tag is unreadable, or
    /// the network is unavailable. A failed check is never surfaced as an error: the user did
    /// not ask for it, and an overlay must not interrupt a game to report that GitHub was slow.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await _client.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, cancellationToken);
            if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName))
            {
                return null;
            }

            if (!TryParseTag(release.TagName, out var version) || version <= CurrentVersion)
            {
                return null;
            }

            var url = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl;
            return new UpdateInfo(release.TagName, version, url);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Accepts the usual <c>v1.2.3</c> release tag as well as a bare <c>1.2.3</c>.</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        var trimmed = tag.Trim().TrimStart('v', 'V');
        var cut = trimmed.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            trimmed = trimmed[..cut];
        }

        return Version.TryParse(trimmed, out version!);
    }

    /// <summary>Hands the release page to the default browser. DriftDeck never installs anything itself.</summary>
    public static void OpenReleasePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser is registered. Nothing useful is left to try.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.Dispose();
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }
}
