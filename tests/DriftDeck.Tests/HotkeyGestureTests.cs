using System.Windows.Input;
using DriftDeck.Services;
using Xunit;

namespace DriftDeck.Tests;

/// <summary>
/// A global hotkey the user types into Settings has to survive a round trip through text, and
/// combinations Windows owns have to be rejected before RegisterHotKey silently fails on them.
/// </summary>
public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+O", ModifierKeys.Control | ModifierKeys.Alt, Key.O)]
    [InlineData("ctrl+alt+o", ModifierKeys.Control | ModifierKeys.Alt, Key.O)]
    [InlineData(" Control + Shift + F5 ", ModifierKeys.Control | ModifierKeys.Shift, Key.F5)]
    [InlineData("Alt+D1", ModifierKeys.Alt, Key.D1)]
    public void TryParse_accepts_a_valid_gesture(string text, ModifierKeys modifiers, Key key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(new HotkeyGesture(modifiers, key), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Ctrl")]
    public void TryParse_rejects_anything_without_a_modifier_and_a_key(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.Contains("at least one modifier", error);
    }

    [Fact]
    public void TryParse_rejects_an_unknown_modifier()
    {
        Assert.False(HotkeyGesture.TryParse("Hyper+O", out _, out var error));
        Assert.Contains("Unknown modifier", error);
    }

    [Fact]
    public void TryParse_rejects_an_unknown_key()
    {
        Assert.False(HotkeyGesture.TryParse("Ctrl+Sparkle", out _, out var error));
        Assert.Contains("Unknown key", error);
    }

    [Fact]
    public void TryParse_rejects_the_Windows_key()
    {
        // RegisterHotKey will not take these, so failing in Settings is the only place the
        // user can be told why.
        Assert.False(HotkeyGesture.TryParse("Win+O", out _, out var error));
        Assert.Contains("reserved for the operating system", error);
    }

    [Theory]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Space")]
    [InlineData("Alt+F4")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Alt+Delete")]
    public void TryParse_rejects_combinations_Windows_owns(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.Contains("reserved by Windows", error);
    }

    [Theory]
    [InlineData("Ctrl+Alt+O")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Alt+H")]
    public void ToString_round_trips_through_TryParse(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out _));
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again, out _));
        Assert.Equal(gesture, again);
    }

    [Fact]
    public void ToString_orders_modifiers_the_way_Windows_writes_them()
    {
        var gesture = new HotkeyGesture(
            ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control,
            Key.O);
        Assert.Equal("Ctrl+Alt+Shift+O", gesture.ToString());
    }

    [Fact]
    public void NativeModifiers_matches_the_RegisterHotKey_flags()
    {
        // MOD_NOREPEAT (0x4000) is always set: a held-down overlay toggle must fire once.
        Assert.Equal(0x4000u, new HotkeyGesture(ModifierKeys.None, Key.O).NativeModifiers);
        Assert.Equal(0x4000u | 0x0001, new HotkeyGesture(ModifierKeys.Alt, Key.O).NativeModifiers);
        Assert.Equal(0x4000u | 0x0002, new HotkeyGesture(ModifierKeys.Control, Key.O).NativeModifiers);
        Assert.Equal(0x4000u | 0x0004, new HotkeyGesture(ModifierKeys.Shift, Key.O).NativeModifiers);
        Assert.Equal(0x4000u | 0x0008, new HotkeyGesture(ModifierKeys.Windows, Key.O).NativeModifiers);
        Assert.Equal(
            0x4000u | 0x0001 | 0x0002,
            new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Alt, Key.O).NativeModifiers);
    }

    [Fact]
    public void VirtualKey_maps_to_the_Win32_code()
    {
        Assert.Equal(0x4Fu, new HotkeyGesture(ModifierKeys.Control, Key.O).VirtualKey);
        Assert.Equal(0x70u, new HotkeyGesture(ModifierKeys.Control, Key.F1).VirtualKey);
    }
}
