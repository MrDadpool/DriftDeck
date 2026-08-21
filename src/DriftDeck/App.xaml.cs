using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DriftDeck.Services;

namespace DriftDeck;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\DriftDeck.SingleInstance";
    internal static readonly uint ShowOverlayMessage = RegisterWindowMessage("DriftDeck.ShowOverlay");
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    /// <summary>
    /// Tracks whether this run ended cleanly and owns the crash log. Created before any window,
    /// because a fault during startup is exactly the one worth recording.
    /// </summary>
    internal static SessionSentinel? Sentinel { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        _ownsSingleInstanceMutex = isFirstInstance;
        if (!isFirstInstance)
        {
            var existingWindow = FindWindow(null, "DriftDeck");
            if (existingWindow != nint.Zero)
            {
                PostMessage(existingWindow, ShowOverlayMessage, nint.Zero, nint.Zero);
            }

            Shutdown();
            return;
        }

        Sentinel = new SessionSentinel();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        ApplyMotionPreference();
        base.OnStartup(e);
    }

    /// <summary>
    /// Records the fault and lets it through. Swallowing it would leave an always-on-top window
    /// alive in an unknown state above whatever the user is doing, which is worse than a crash
    /// they can see; the layout on disk is already current, so nothing is lost by going down.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        Sentinel?.WriteCrashReport(e.Exception, "Dispatcher");

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Sentinel?.WriteCrashReport(exception, "AppDomain");
        }
    }

    /// <summary>
    /// Collapses the shared duration tokens to zero when Windows animations are switched off.
    /// Every XAML storyboard binds its Duration to one of these three keys, so this one call
    /// disables declarative motion app-wide; <see cref="Services.Motion"/> covers the code side.
    /// </summary>
    private void ApplyMotionPreference()
    {
        if (Services.Motion.Enabled)
        {
            return;
        }

        var instant = new Duration(TimeSpan.Zero);
        Resources["DurFast"] = instant;
        Resources["DurBase"] = instant;
        Resources["DurSlow"] = instant;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Reached only on a deliberate shutdown, which is precisely what the marker records.
        Sentinel?.Dispose();
        Sentinel = null;

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string messageName);
}
