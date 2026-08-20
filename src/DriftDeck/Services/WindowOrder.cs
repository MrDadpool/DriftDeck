using System.Runtime.InteropServices;

namespace DriftDeck.Services;

/// <summary>
/// Ordering inside the always-on-top band.
/// <para>
/// Every DriftDeck window is topmost, so Windows orders them among themselves by activation
/// alone. Re-asserting <c>Topmost</c> to reorder them causes a visible flash and can steal
/// foreground from the game underneath. <see cref="BringToFront"/> uses the primitive that
/// actually exists for this — SetWindowPos with NOACTIVATE — which reorders without a flash
/// and without touching focus.
/// </para>
/// </summary>
public static class WindowOrder
{
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    public static void BringToFront(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return;
        }

        SetWindowPos(windowHandle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
    }

    /// <summary>Brings the window forward and shows it, still without stealing focus.</summary>
    public static void ShowInFront(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return;
        }

        SetWindowPos(windowHandle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}
