using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DriftDeck.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ToggleInteractionId = 0xD001;
    private const int ToggleVisibilityId = 0xD002;

    /// <summary>Quick-layout ids are this base plus the digit, so slot 3 is 0xD103.</summary>
    private const int QuickLayoutIdBase = 0xD100;

    private readonly nint _windowHandle;
    private readonly HwndSource _source;
    private readonly List<int> _registeredQuickSlots = [];
    private bool _disposed;

    public event EventHandler? InteractionToggleRequested;
    public event EventHandler? VisibilityToggleRequested;

    /// <summary>Raised with the digit that was pressed, 1 to 9.</summary>
    public event EventHandler<int>? QuickLayoutRequested;

    /// <summary>
    /// Digits Windows refused, because something else already owns that combination. Reported
    /// rather than thrown: a taken <c>Ctrl+Alt+4</c> must not cost the user their pass-through
    /// shortcut, which is the one hotkey the overlay cannot work without.
    /// </summary>
    public IReadOnlyList<int> RejectedQuickSlots { get; private set; } = [];

    public GlobalHotkeyService(nint windowHandle, HotkeyGesture interactionGesture, HotkeyGesture visibilityGesture)
        : this(windowHandle, interactionGesture, visibilityGesture, [])
    {
    }

    public GlobalHotkeyService(nint windowHandle, HotkeyGesture interactionGesture,
        HotkeyGesture visibilityGesture, IReadOnlyList<int> quickLayoutSlots)
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

        RegisterQuickLayoutSlots(quickLayoutSlots);
    }

    /// <summary>
    /// Registers <c>Ctrl+Alt+&lt;digit&gt;</c> for each requested slot, collecting refusals
    /// instead of failing. Unlike the two mode shortcuts these are a convenience, and any of
    /// them may already belong to another application.
    /// </summary>
    private void RegisterQuickLayoutSlots(IReadOnlyList<int> slots)
    {
        var rejected = new List<int>();
        foreach (var slot in slots.Distinct())
        {
            if (slot is < 1 or > 9)
            {
                continue;
            }

            var gesture = QuickLayoutGesture(slot);
            if (RegisterHotKey(_windowHandle, QuickLayoutIdBase + slot,
                    gesture.NativeModifiers, gesture.VirtualKey))
            {
                _registeredQuickSlots.Add(slot);
            }
            else
            {
                rejected.Add(slot);
            }
        }

        RejectedQuickSlots = rejected;
    }

    /// <summary>Digits map onto <see cref="Key.D1"/> upward, which is what Windows expects here.</summary>
    public static HotkeyGesture QuickLayoutGesture(int slot) =>
        new(ModifierKeys.Control | ModifierKeys.Alt, Key.D1 + (slot - 1));

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
            default:
                var slot = wParam.ToInt32() - QuickLayoutIdBase;
                if (slot is >= 1 and <= 9)
                {
                    QuickLayoutRequested?.Invoke(this, slot);
                }
                else
                {
                    // Not one of ours. Leave it for whoever registered it.
                    handled = false;
                }

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
        foreach (var slot in _registeredQuickSlots)
        {
            UnregisterHotKey(_windowHandle, QuickLayoutIdBase + slot);
        }

        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
