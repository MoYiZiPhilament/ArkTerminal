namespace ArkTerminal.Terminal.Input;

public enum TerminalKey
{
    Enter,
    Backspace,
    Tab,
    Escape,
    Up,
    Down,
    Right,
    Left,
    Home,
    End,
    Insert,
    Delete,
    PageUp,
    PageDown,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12
}

public static class TerminalInputEncoder
{
    public static string EncodeKey(TerminalKey key, bool control, bool alt, bool applicationCursor)
    {
        var encoded = key switch
        {
            TerminalKey.Enter => "\r",
            TerminalKey.Backspace => "\u007f",
            TerminalKey.Tab => control ? "\u001b[Z" : "\t",
            TerminalKey.Escape => "\u001b",
            TerminalKey.Up => CursorSequence("A", applicationCursor, control, alt),
            TerminalKey.Down => CursorSequence("B", applicationCursor, control, alt),
            TerminalKey.Right => CursorSequence("C", applicationCursor, control, alt),
            TerminalKey.Left => CursorSequence("D", applicationCursor, control, alt),
            TerminalKey.Home => ModifiedSequence("H", control, alt),
            TerminalKey.End => ModifiedSequence("F", control, alt),
            TerminalKey.Insert => ModifiedTildeSequence(2, control, alt),
            TerminalKey.Delete => ModifiedTildeSequence(3, control, alt),
            TerminalKey.PageUp => ModifiedTildeSequence(5, control, alt),
            TerminalKey.PageDown => ModifiedTildeSequence(6, control, alt),
            TerminalKey.F1 => FunctionSequence("P", control, alt),
            TerminalKey.F2 => FunctionSequence("Q", control, alt),
            TerminalKey.F3 => FunctionSequence("R", control, alt),
            TerminalKey.F4 => FunctionSequence("S", control, alt),
            TerminalKey.F5 => ModifiedTildeSequence(15, control, alt),
            TerminalKey.F6 => ModifiedTildeSequence(17, control, alt),
            TerminalKey.F7 => ModifiedTildeSequence(18, control, alt),
            TerminalKey.F8 => ModifiedTildeSequence(19, control, alt),
            TerminalKey.F9 => ModifiedTildeSequence(20, control, alt),
            TerminalKey.F10 => ModifiedTildeSequence(21, control, alt),
            TerminalKey.F11 => ModifiedTildeSequence(23, control, alt),
            TerminalKey.F12 => ModifiedTildeSequence(24, control, alt),
            _ => string.Empty
        };
        return alt && key is TerminalKey.Enter or TerminalKey.Backspace or TerminalKey.Tab or TerminalKey.Escape
            ? "\u001b" + encoded
            : encoded;
    }

    public static string EncodeControlCharacter(char character, bool alt)
    {
        var value = char.ToUpperInvariant(character) switch
        {
            ' ' or '@' => 0,
            >= 'A' and <= 'Z' => char.ToUpperInvariant(character) - 'A' + 1,
            '[' => 27,
            '\\' => 28,
            ']' => 29,
            '^' => 30,
            '_' => 31,
            '?' => 127,
            _ => -1
        };
        if (value < 0)
        {
            return string.Empty;
        }
        return (alt ? "\u001b" : string.Empty) + (char)value;
    }

    public static string EncodeWin32ControlCharacter(char character)
        => EncodeWin32Character(character, control: true, alt: false, shift: false);

    public static string EncodeWin32Character(char character, bool control, bool alt, bool shift)
    {
        var upper = char.ToUpperInvariant(character);
        if (upper is >= 'A' and <= 'Z')
        {
            var unicode = control ? upper - 'A' + 1 : alt ? 0 : shift ? upper : char.ToLowerInvariant(upper);
            return EncodeWin32Records(upper, LetterScanCode(upper), unicode, control, alt, shift, enhanced: false);
        }
        if (character == ' ')
        {
            return EncodeWin32Records(32, 57, control ? 0 : 32, control, alt, shift, enhanced: false);
        }
        return string.Empty;
    }

    public static string EncodeWin32Key(TerminalKey key, bool control, bool alt, bool shift)
    {
        int vk, scan, unicode;
        var enhanced = false;
        switch (key)
        {
            case TerminalKey.Enter: vk = 13; scan = 28; unicode = control ? 10 : 13; break;
            case TerminalKey.Backspace: vk = 8; scan = 14; unicode = control ? 127 : 8; break;
            case TerminalKey.Tab: vk = 9; scan = 15; unicode = 9; break;
            case TerminalKey.Escape: vk = 27; scan = 1; unicode = 27; break;
            case TerminalKey.Up: vk = 38; scan = 72; unicode = 0; enhanced = true; break;
            case TerminalKey.Down: vk = 40; scan = 80; unicode = 0; enhanced = true; break;
            case TerminalKey.Right: vk = 39; scan = 77; unicode = 0; enhanced = true; break;
            case TerminalKey.Left: vk = 37; scan = 75; unicode = 0; enhanced = true; break;
            case TerminalKey.Home: vk = 36; scan = 71; unicode = 0; enhanced = true; break;
            case TerminalKey.End: vk = 35; scan = 79; unicode = 0; enhanced = true; break;
            case TerminalKey.Insert: vk = 45; scan = 82; unicode = 0; enhanced = true; break;
            case TerminalKey.Delete: vk = 46; scan = 83; unicode = 0; enhanced = true; break;
            case TerminalKey.PageUp: vk = 33; scan = 73; unicode = 0; enhanced = true; break;
            case TerminalKey.PageDown: vk = 34; scan = 81; unicode = 0; enhanced = true; break;
            case TerminalKey.F1: vk = 112; scan = 59; unicode = 0; break;
            case TerminalKey.F2: vk = 113; scan = 60; unicode = 0; break;
            case TerminalKey.F3: vk = 114; scan = 61; unicode = 0; break;
            case TerminalKey.F4: vk = 115; scan = 62; unicode = 0; break;
            case TerminalKey.F5: vk = 116; scan = 63; unicode = 0; break;
            case TerminalKey.F6: vk = 117; scan = 64; unicode = 0; break;
            case TerminalKey.F7: vk = 118; scan = 65; unicode = 0; break;
            case TerminalKey.F8: vk = 119; scan = 66; unicode = 0; break;
            case TerminalKey.F9: vk = 120; scan = 67; unicode = 0; break;
            case TerminalKey.F10: vk = 121; scan = 68; unicode = 0; break;
            case TerminalKey.F11: vk = 122; scan = 87; unicode = 0; break;
            case TerminalKey.F12: vk = 123; scan = 88; unicode = 0; break;
            default: return string.Empty;
        }
        return EncodeWin32Records(vk, scan, unicode, control, alt, shift, enhanced);
    }

    private static int LetterScanCode(char upper) => upper switch
    {
        'A' => 30, 'B' => 48, 'C' => 46, 'D' => 32, 'E' => 18, 'F' => 33,
        'G' => 34, 'H' => 35, 'I' => 23, 'J' => 36, 'K' => 37, 'L' => 38,
        'M' => 50, 'N' => 49, 'O' => 24, 'P' => 25, 'Q' => 16, 'R' => 19,
        'S' => 31, 'T' => 20, 'U' => 22, 'V' => 47, 'W' => 17, 'X' => 45,
        'Y' => 21, 'Z' => 44, _ => 0
    };

    // Modifier-down (Ctrl, Alt, Shift) -> key down -> key up -> modifier-up (reverse),
    // matching the console INPUT_RECORD stream a physical key press produces.
    private static string EncodeWin32Records(int vk, int scan, int unicode, bool control, bool alt, bool shift, bool enhanced)
    {
        var builder = new System.Text.StringBuilder();
        var state = 0;
        if (control) { state |= 8; AppendWin32Record(builder, 17, 29, 0, 1, state); }
        if (alt) { state |= 2; AppendWin32Record(builder, 18, 56, 0, 1, state); }
        if (shift) { state |= 16; AppendWin32Record(builder, 16, 42, 0, 1, state); }
        var keyState = state | (enhanced ? 256 : 0);
        AppendWin32Record(builder, vk, scan, unicode, 1, keyState);
        AppendWin32Record(builder, vk, scan, unicode, 0, keyState);
        if (shift) { state &= ~16; AppendWin32Record(builder, 16, 42, 0, 0, state); }
        if (alt) { state &= ~2; AppendWin32Record(builder, 18, 56, 0, 0, state); }
        if (control) { state &= ~8; AppendWin32Record(builder, 17, 29, 0, 0, state); }
        return builder.ToString();
    }

    private static void AppendWin32Record(System.Text.StringBuilder builder, int vk, int scan, int unicode, int keyDown, int controlState)
        => builder.Append((char)27).Append($"[{vk};{scan};{unicode};{keyDown};{controlState};1_");

    private static string CursorSequence(string final, bool application, bool control, bool alt)
    {
        var modifier = Modifier(control, alt);
        if (modifier > 1)
        {
            return $"\u001b[1;{modifier}{final}";
        }
        return application ? $"\u001bO{final}" : $"\u001b[{final}";
    }

    private static string ModifiedSequence(string final, bool control, bool alt) =>
        Modifier(control, alt) > 1 ? $"\u001b[1;{Modifier(control, alt)}{final}" : $"\u001b[{final}";

    private static string ModifiedTildeSequence(int code, bool control, bool alt) =>
        Modifier(control, alt) > 1 ? $"\u001b[{code};{Modifier(control, alt)}~" : $"\u001b[{code}~";

    private static string FunctionSequence(string final, bool control, bool alt) =>
        Modifier(control, alt) > 1 ? $"\u001b[1;{Modifier(control, alt)}{final}" : $"\u001bO{final}";

    private static int Modifier(bool control, bool alt) => 1 + (alt ? 1 : 0) + (control ? 4 : 0);
}
