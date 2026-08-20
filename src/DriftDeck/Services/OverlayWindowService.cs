using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DriftDeck.Services;

public sealed class OverlayWindowService : IDisposable
{
    private const int ExtendedStyleIndex = -20;
    private const long TransparentStyle = 0x00000020L;
    private const long NoActivateStyle = 0x08000000L;
    private const int WmNcHitTest = 0x0084;
    private static readonly nint HitTestTransparent = new(-1);

    private readonly nint _windowHandle;
    private readonly HwndSource? _source;
    private bool _clickThrough;
    private bool _disposed;

    public OverlayWindowService(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WindowProcedure);
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        var styles = GetWindowLongPtr(_windowHandle, ExtendedStyleIndex).ToInt64();
        styles = enabled
            ? styles | TransparentStyle | NoActivateStyle
            : styles & ~(TransparentStyle | NoActivateStyle);
        SetWindowLongPtr(_windowHandle, ExtendedStyleIndex, new nint(styles));
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (_clickThrough && message == WmNcHitTest)
        {
            handled = true;
            return HitTestTransparent;
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _source?.RemoveHook(WindowProcedure);
        _disposed = true;
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index) =>
        Environment.Is64BitProcess
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint newValue) =>
        Environment.Is64BitProcess
            ? SetWindowLongPtr64(windowHandle, index, newValue)
            : new nint(SetWindowLong32(windowHandle, index, newValue.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(nint windowHandle, int index, int newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint newValue);
}
