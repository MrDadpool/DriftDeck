using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DriftDeck.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ToggleInteractionId = 0xD001;
    private const int ToggleVisibilityId = 0xD002;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private bool _disposed;

    public event EventHandler? InteractionToggleRequested;
    public event EventHandler? VisibilityToggleRequested;

    public GlobalHotkeyService(nint windowHandle, HotkeyGesture interactionGesture, HotkeyGesture visibilityGesture)
    {
        if (interactionGesture == visibilityGesture)
        {
            throw new ArgumentException("Interaction and visibility shortcuts must be different.");
        }

        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle)
                  ?? throw new InvalidOperationException("The overlay window is not ready.");
        _source.AddHook(WindowProcedure);

        Register(ToggleInteractionId, interactionGesture);
        try
        {
            Register(ToggleVisibilityId, visibilityGesture);
        }
        catch
        {
            UnregisterHotKey(_windowHandle, ToggleInteractionId);
            _source.RemoveHook(WindowProcedure);
            throw;
        }
    }

    private void Register(int id, HotkeyGesture gesture)
    {
        if (!RegisterHotKey(_windowHandle, id, gesture.NativeModifiers, gesture.VirtualKey))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not register {gesture}.");
        }
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmHotkey)
        {
            return nint.Zero;
        }

        handled = true;
        switch (wParam.ToInt32())
        {
            case ToggleInteractionId:
                InteractionToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
            case ToggleVisibilityId:
                VisibilityToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source.RemoveHook(WindowProcedure);
        UnregisterHotKey(_windowHandle, ToggleInteractionId);
        UnregisterHotKey(_windowHandle, ToggleVisibilityId);
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
