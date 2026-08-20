using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DriftDeck.Services;

/// <summary>
/// Work-area lookup in WPF device-independent units, so panels snap and clamp to the
/// monitor they are actually on rather than to the whole virtual desktop.
/// </summary>
public static class MonitorHelper
{
    private const uint MonitorDefaultToNearest = 2;

    public static Rect WorkAreaForWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = handle == nint.Zero
            ? nint.Zero
            : MonitorFromWindow(handle, MonitorDefaultToNearest);
        return ToWorkArea(monitor, window);
    }

    public static Rect WorkAreaForPoint(Point deviceIndependentPoint, Window reference)
    {
        var source = PresentationSource.FromVisual(reference);
        var devicePoint = source?.CompositionTarget is null
            ? deviceIndependentPoint
            : source.CompositionTarget.TransformToDevice.Transform(deviceIndependentPoint);
        var monitor = MonitorFromPoint(
            new NativePoint { X = (int)devicePoint.X, Y = (int)devicePoint.Y }, MonitorDefaultToNearest);
        return ToWorkArea(monitor, reference);
    }

    /// <summary>Full virtual desktop, used as the fallback when no monitor can be resolved.</summary>
    public static Rect VirtualScreen => new(
        SystemParameters.VirtualScreenLeft,
        SystemParameters.VirtualScreenTop,
        SystemParameters.VirtualScreenWidth,
        SystemParameters.VirtualScreenHeight);

    private static Rect ToWorkArea(nint monitor, Visual reference)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return VirtualScreen;
        }

        var target = PresentationSource.FromVisual(reference)?.CompositionTarget;
        if (target is null)
        {
            return new Rect(
                info.WorkArea.Left, info.WorkArea.Top,
                info.WorkArea.Right - info.WorkArea.Left,
                info.WorkArea.Bottom - info.WorkArea.Top);
        }

        var topLeft = target.TransformFromDevice.Transform(new Point(info.WorkArea.Left, info.WorkArea.Top));
        var bottomRight = target.TransformFromDevice.Transform(new Point(info.WorkArea.Right, info.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);
}
