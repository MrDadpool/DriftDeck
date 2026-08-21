using System.IO;

namespace DriftDeck.Services;

/// <summary>
/// Marks a run as in-progress on disk and clears the mark on a clean exit, so the next launch
/// can tell "the user quit" from "the process died".
/// <para>
/// An overlay is unusually likely to be killed rather than closed: it sits above a game, and a
/// game that hangs is normally cleared with Task Manager, which takes DriftDeck with it. The
/// layout itself is already durable - every change is written within about 650 ms - so recovery
/// here is not about restoring data but about saying what happened instead of silently
/// pretending the last session ended normally.
/// </para>
/// </summary>
public sealed class SessionSentinel : IDisposable
{
    private readonly string _markerPath;
    private readonly string _logDirectory;
    private bool _disposed;

    /// <summary>True when the previous run left its marker behind, meaning it never exited cleanly.</summary>
    public bool PreviousRunCrashed { get; }

    /// <summary>When the interrupted run last wrote its marker, or null if that is unknown.</summary>
    public DateTime? PreviousRunEndedAt { get; }

    public SessionSentinel()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(appData, "DriftDeck");
        _markerPath = Path.Combine(root, "session.lock");
        _logDirectory = Path.Combine(root, "logs");

        try
        {
            Directory.CreateDirectory(root);
            if (File.Exists(_markerPath))
            {
                PreviousRunCrashed = true;
                PreviousRunEndedAt = File.GetLastWriteTime(_markerPath);
            }

            File.WriteAllText(_markerPath, DateTime.Now.ToString("O"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Crash reporting is a courtesy. It must never be the reason the overlay fails to start.
        }
    }

    public string LogDirectory => _logDirectory;

    /// <summary>Appends a fault to today's log and returns the file written, or null if logging failed.</summary>
    public string? WriteCrashReport(Exception exception, string source)
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
            var path = Path.Combine(_logDirectory, $"crash-{DateTime.Now:yyyy-MM-dd}.log");
            var report = $"""

                ===== {DateTime.Now:O} =====
                Source:  {source}
                Version: {UpdateService.CurrentVersion}
                OS:      {Environment.OSVersion}
                {exception}

                """;
            File.AppendAllText(path, report);
            return path;
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes the marker. Called only on a deliberate shutdown.</summary>
    public void MarkCleanExit()
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A stale marker only costs one incorrect recovery notice on the next launch.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MarkCleanExit();
    }
}
