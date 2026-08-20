using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DriftDeck;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\DriftDeck.SingleInstance";
    internal static readonly uint ShowOverlayMessage = RegisterWindowMessage("DriftDeck.ShowOverlay");
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

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

        ApplyMotionPreference();
        base.OnStartup(e);
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
