using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DriftDeck.Services;

/// <summary>
/// Reports when Windows says a Direct3D application currently owns the screen in exclusive
/// fullscreen, because that is the one configuration where an ordinary always-on-top window
/// cannot be drawn over and DriftDeck looks broken through no fault of its own.
/// <para>
/// The whole mechanism is a single call to <c>SHQueryUserNotificationState</c>, the same shell
/// API Windows itself uses to decide whether a toast may appear. It names no process, opens no
/// handle, reads no memory, and installs no hook — it answers exactly one question about the
/// desktop's own presentation state. Saying "switch to borderless" is strictly better than
/// leaving the user to conclude the overlay does not work.
/// </para>
/// </summary>
public sealed class FullscreenProbe : IDisposable
{
    /// <summary>Values of <c>QUERY_USER_NOTIFICATION_STATE</c> that matter here.</summary>
    private const int RunningD3DFullScreen = 3;

    private readonly DispatcherTimer _timer;
    private bool _lastBlocking;
    private bool _disposed;

    /// <summary>
    /// Raised only when the answer changes, so the status strip is written once per transition
    /// rather than once per poll.
    /// </summary>
    public event EventHandler<bool>? BlockingChanged;

    public FullscreenProbe()
    {
        // Slower than the foreground poll: this only has to catch a mode change, and a game
        // entering exclusive fullscreen is not a per-second event.
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>True while the desktop reports an exclusive-fullscreen D3D application.</summary>
    public bool IsBlocking => _lastBlocking;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>Reads the state now. False whenever the shell declines to answer.</summary>
    public static bool QueryBlocking()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0 && state == RunningD3DFullScreen;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private void Poll()
    {
        var blocking = QueryBlocking();
        if (blocking == _lastBlocking)
        {
            return;
        }

        _lastBlocking = blocking;
        BlockingChanged?.Invoke(this, blocking);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
