using System.Windows.Input;

namespace DriftDeck.Services;

public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public uint NativeModifiers
    {
        get
        {
            uint value = 0x4000; // MOD_NOREPEAT
            if (Modifiers.HasFlag(ModifierKeys.Alt)) value |= 0x0001;
            if (Modifiers.HasFlag(ModifierKeys.Control)) value |= 0x0002;
            if (Modifiers.HasFlag(ModifierKeys.Shift)) value |= 0x0004;
            if (Modifiers.HasFlag(ModifierKeys.Windows)) value |= 0x0008;
            return value;
        }
    }

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string? text, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = string.Empty;
        var tokens = (text ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = "Use at least one modifier and one key, such as Ctrl+Alt+O.";
            return false;
        }

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            switch (tokens[index].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL": modifiers |= ModifierKeys.Control; break;
                case "ALT": modifiers |= ModifierKeys.Alt; break;
                case "SHIFT": modifiers |= ModifierKeys.Shift; break;
                case "WIN":
                case "WINDOWS": modifiers |= ModifierKeys.Windows; break;
                default:
                    error = $"Unknown modifier '{tokens[index]}'. Use Ctrl, Alt, Shift, or Win.";
                    return false;
            }
        }

        if (!Enum.TryParse(tokens[^1], true, out Key key) || key is Key.None or Key.System)
        {
            error = $"Unknown key '{tokens[^1]}'.";
            return false;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            error = "Windows-key shortcuts are reserved for the operating system. Use Ctrl, Alt, or Shift.";
            return false;
        }

        if ((key == Key.Space && modifiers.HasFlag(ModifierKeys.Alt)) ||
            (key == Key.Tab && modifiers.HasFlag(ModifierKeys.Alt)) ||
            (key == Key.F4 && modifiers.HasFlag(ModifierKeys.Alt)) ||
            (key == Key.Escape && modifiers.HasFlag(ModifierKeys.Control)) ||
            (key == Key.Delete && modifiers.HasFlag(ModifierKeys.Control) && modifiers.HasFlag(ModifierKeys.Alt)) ||
            (key == Key.L && modifiers.HasFlag(ModifierKeys.Windows)))
        {
            error = "That shortcut is reserved by Windows. Choose another combination.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}
