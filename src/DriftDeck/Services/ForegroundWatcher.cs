using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace DriftDeck.Services;

/// <summary>The foreground window as Windows reports it, reduced to what a rule can match on.</summary>
public readonly record struct ForegroundApp(string ProcessName, string WindowTitle)
{
    public bool IsEmpty => string.IsNullOrEmpty(ProcessName);

    /// <summary>Identity used to tell "the user switched applications" from "the title ticked over".</summary>
    public string Key => $"{ProcessName} {WindowTitle}";
}

/// <summary>
/// Polls the foreground window once a second and reports the owning process name and title.
/// <para>
/// This is deliberately the weakest mechanism that answers "which application is the user
/// looking at": <c>GetForegroundWindow</c>, <c>GetWindowThreadProcessId</c>, and
/// <c>GetWindowText</c> are the same read-only calls the taskbar makes. No process is opened
/// for memory access, no code is injected, no hook is installed inside another process, and no
/// window is modified. A one-second poll was chosen over <c>SetWinEventHook</c> because it needs
/// no cross-process callback at all and still catches title changes, which a foreground-only
/// hook misses.
/// </para>
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private string _lastKey = string.Empty;
    private bool _disposed;

    /// <summary>Raised when the foreground process or its title changes. Never raised for DriftDeck itself.</summary>
    public event EventHandler<ForegroundApp>? Changed;

    public ForegroundWatcher()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Poll();
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>Reads the foreground application right now, or an empty value if it is DriftDeck.</summary>
    public ForegroundApp Current()
    {
        var handle = GetForegroundWindow();
        if (handle == nint.Zero)
        {
            return default;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == _ownProcessId)
        {
            return default;
        }

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The window closed between the two calls. Nothing to report this tick.
            return default;
        }

        return new ForegroundApp(processName, ReadTitle(handle));
    }

    private void Poll()
    {
        var current = Current();
        if (current.IsEmpty || current.Key == _lastKey)
        {
            return;
        }

        _lastKey = current.Key;
        Changed?.Invoke(this, current);
    }

    private static string ReadTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        return GetWindowText(handle, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
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

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint windowHandle, StringBuilder text, int maxCount);
}
