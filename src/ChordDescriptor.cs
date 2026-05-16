using System.Text;
using System.Windows.Forms;

namespace SpeechToText;

[Flags]
internal enum ChordModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Win = 8,
}

internal readonly record struct ChordDescriptor(ChordModifiers Modifiers, Keys Key)
{
    public bool IsValid => Modifiers != ChordModifiers.None && Key != Keys.None && !IsModifierKey(Key);

    public override string ToString()
    {
        if (Key == Keys.None && Modifiers == ChordModifiers.None) return "";
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ChordModifiers.Ctrl)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ChordModifiers.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ChordModifiers.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ChordModifiers.Win)) sb.Append("Win+");
        sb.Append(FormatKey(Key));
        return sb.ToString();
    }

    public static bool TryParse(string? text, out ChordDescriptor chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var mods = ChordModifiers.None;
        Keys key = Keys.None;
        for (int i = 0; i < parts.Length; i++)
        {
            var token = parts[i];
            bool isLast = i == parts.Length - 1;
            if (!isLast)
            {
                var m = ParseModifier(token);
                if (m == ChordModifiers.None) return false;
                if (mods.HasFlag(m)) return false;
                mods |= m;
            }
            else
            {
                if (!TryParseKey(token, out key)) return false;
            }
        }

        var result = new ChordDescriptor(mods, key);
        if (!result.IsValid) return false;
        chord = result;
        return true;
    }

    public static ChordDescriptor Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"Invalid chord: '{text}'");

    private static ChordModifiers ParseModifier(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => ChordModifiers.Ctrl,
        "shift" => ChordModifiers.Shift,
        "alt" or "menu" => ChordModifiers.Alt,
        "win" or "windows" or "meta" or "super" => ChordModifiers.Win,
        _ => ChordModifiers.None,
    };

    private static bool TryParseKey(string token, out Keys key)
    {
        // Single digit "0"-"9" maps to D0..D9 (not the numeric Keys enum value).
        if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
        {
            key = Keys.D0 + (token[0] - '0');
            return true;
        }

        // Friendly aliases.
        switch (token.ToLowerInvariant())
        {
            case "esc": key = Keys.Escape; return true;
            case "ins": key = Keys.Insert; return true;
            case "del": key = Keys.Delete; return true;
            case "pgup": key = Keys.PageUp; return true;
            case "pgdn": key = Keys.PageDown; return true;
            case "plus": key = Keys.Oemplus; return true;
            case "minus": key = Keys.OemMinus; return true;
        }

        if (Enum.TryParse(token, ignoreCase: true, out key) && key != Keys.None && !IsModifierKey(key))
            return true;

        key = Keys.None;
        return false;
    }

    private static string FormatKey(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((int)(key - Keys.D0)).ToString(),
        _ => key.ToString(),
    };

    private static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
        Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin or
        // The Keys enum overloads names with modifier-flag aliases (Shift = 0x10000
        // etc). Those aren't real key codes — reject them here so Enum.TryParse on
        // "Shift" / "Control" / "Alt" doesn't sneak through as a key.
        Keys.Shift or Keys.Control or Keys.Alt;
}
