using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace DriftDeck.Services;

/// <summary>
/// The pointer's position on the virtual desktop, in device-independent units.
/// <para>
/// Idle dimming cannot rely on WPF mouse events alone: browser panels host their content in a
/// composition surface, so a pointer resting over a web page raises nothing on the WPF side and
/// a panel the user is actively reading would fade out. Asking Windows where the cursor is
/// covers that case with one read-only call.
/// </para>
/// </summary>
public static class CursorProbe
{
    /// <summary>Cursor position converted through <paramref name="reference"/>'s DPI, or null if unavailable.</summary>
    public static Point? Position(Visual? reference)
    {
        if (!GetCursorPos(out var point))
        {
            return null;
        }

        var raw = new Point(point.X, point.Y);
        if (reference is null)
        {
            return raw;
        }

        var target = PresentationSource.FromVisual(reference)?.CompositionTarget;
        return target is null ? raw : target.TransformFromDevice.Transform(raw);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
}
