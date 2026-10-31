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
    {
        var upper = char.ToUpperInvariant(character);
        if (upper is < 'A' or > 'Z')
        {
            return string.Empty;
        }
        var unicode = upper - 'A' + 1;
        var scanCode = upper switch
        {
            'A' => 30, 'B' => 48, 'C' => 46, 'D' => 32, 'E' => 18, 'F' => 33,
            'G' => 34, 'H' => 35, 'I' => 23, 'J' => 36, 'K' => 37, 'L' => 38,
            'M' => 50, 'N' => 49, 'O' => 24, 'P' => 25, 'Q' => 16, 'R' => 19,
            'S' => 31, 'T' => 20, 'U' => 22, 'V' => 47, 'W' => 17, 'X' => 45,
            'Y' => 21, 'Z' => 44, _ => 0
        };
        return $"\u001b[17;29;0;1;8;1_\u001b[{(int)upper};{scanCode};{unicode};1;8;1_" +
               $"\u001b[{(int)upper};{scanCode};{unicode};0;8;1_\u001b[17;29;0;0;0;1_";
    }

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
