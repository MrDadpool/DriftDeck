using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace DriftDeck.Services;

/// <summary>A newer published release than the one running.</summary>
public sealed record UpdateInfo(string Tag, Version Version, Velopack.UpdateInfo Package);

/// <summary>
/// Finds, downloads, and applies updates through Velopack, reading the public GitHub releases.
/// <para>
/// The check is an anonymous read of the public release feed. Nothing about the user, the
/// machine, or the applications DriftDeck is running over is sent. Every step past the check is
/// the user's call: nothing downloads until they ask, and DriftDeck only restarts when they press
/// the button that says so — an overlay that restarts itself mid-game is worse than an old one.
/// </para>
/// <para>
/// A build run from source, or any copy not installed by Setup, has no Velopack install to update,
/// so every method reports "no update" there rather than failing.
/// </para>
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "MrDadpool/DriftDeck";
    public const string RepositoryUrl = $"https://github.com/{Repository}";

    private readonly UpdateManager _manager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    /// <summary>The running build, taken from the assembly so the csproj stays the single source.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>False for a build that Setup did not install, which has nothing to update.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>
    /// Returns the newer release, or null when the build is current, not installed, or the
    /// network is unavailable. A failed check is never surfaced as an error: the user did not ask
    /// for it, and an overlay must not interrupt a game to report that GitHub was slow.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        try
        {
            var package = await _manager.CheckForUpdatesAsync();
            if (package is null || package.IsDowngrade)
            {
                return null;
            }

            var version = package.TargetFullRelease.Version;
            return new UpdateInfo($"v{version}", new Version(version.Major, version.Minor, version.Patch), package);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Velopack surfaces network, feed, and rate-limit failures as assorted exception
            // types. All of them mean the same thing here: no update to offer right now.
            return null;
        }
    }

    /// <summary>Downloads the release, reporting progress from 0 to 100.</summary>
    public Task DownloadAsync(UpdateInfo update, Action<int>? progress, CancellationToken cancellationToken = default) =>
        _manager.DownloadUpdatesAsync(update.Package, progress, cancellationToken);

    /// <summary>
    /// Hands a downloaded release to Velopack's updater, which waits for DriftDeck to exit, swaps
    /// in the new version, and starts it again. The caller then closes the app the ordinary way, so
    /// the layout is saved and the session is marked as a clean exit before the files change.
    /// </summary>
    public void ApplyAfterExit(UpdateInfo update) =>
        _manager.WaitExitThenApplyUpdates(update.Package.TargetFullRelease, silent: false, restart: true);
}
