using Microsoft.Win32;
using System.Windows.Threading;

namespace DriftDeck.Services;

/// <summary>
/// Raises one debounced event whenever the set of monitors, their resolution, their scaling, or
/// the session itself changes.
/// <para>
/// A saved layout is a list of virtual-desktop coordinates. Undock a laptop, switch a TV off,
/// let a GPU driver reset, or resume from sleep, and those coordinates can name a monitor that
/// no longer exists — the panels are still there, just nowhere the user can reach them. Windows
/// announces every one of these through <see cref="SystemEvents"/>, so the fix is to listen and
/// re-clamp rather than to wait for the user to notice.
/// </para>
/// <para>
/// <see cref="SystemEvents"/> fires on its own thread and holds a static, process-wide
/// subscription list, so handlers are marshalled to the dispatcher and unsubscribed on dispose;
/// a leaked handler here keeps the window alive for the life of the process.
/// </para>
/// </summary>
public sealed class DisplayWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _settle;
    private bool _disposed;

    /// <summary>Raised once the display change has stopped arriving in bursts.</summary>
    public event EventHandler? Changed;

    public DisplayWatcher()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;

        // A resolution change arrives as several notifications in a row, and a docking change
        // arrives as one per monitor. Re-clamping on each would fight the driver mid-transition.
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        SystemEvents.DisplaySettingsChanged += OnSystemEvent;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSystemEvent(object? sender, EventArgs e) => Schedule();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Resume is the case that matters: monitors come back in an order the layout may predate.
        if (e.Mode == PowerModes.Resume)
        {
            Schedule();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        if (_disposed)
        {
            return;
        }

        // These arrive on a system thread; window geometry may only be touched on the dispatcher.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _settle.Stop();
            _settle.Start();
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settle.Stop();
        SystemEvents.DisplaySettingsChanged -= OnSystemEvent;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
