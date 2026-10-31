using System.Globalization;
using System.Text;
using ArkTerminal.Terminal.Rendering;
using ArkTerminal.Terminal.Screen;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Terminal.Vt;

internal enum ParserState
{
    Ground,
    Escape,
    Csi,
    IgnoreCsi,
    Osc,
    OscEscape,
    IgnoreString,
    IgnoreStringEscape,
    IgnoreOne
}

internal sealed class VtParser
{
    private readonly TerminalScreen _screen;
    private readonly TerminalSessionOptions _options;
    private readonly TerminalDiagnostics _diagnostics;
    private readonly Action<ReadOnlyMemory<byte>> _respond;
    private readonly StringBuilder _text = new();
    private readonly IncrementalTextElementSegmenter _textElements = new();
    private readonly StringBuilder _sequence = new();
    private ParserState _state;
    private TerminalStyle _style = TerminalStyle.Default;

    internal VtParser(
        TerminalScreen screen,
        TerminalSessionOptions options,
        TerminalDiagnostics diagnostics,
        Action<ReadOnlyMemory<byte>> respond)
    {
        _screen = screen;
        _options = options;
        _diagnostics = diagnostics;
        _respond = respond;
    }

    internal void Write(ReadOnlySpan<char> input)
    {
        foreach (var character in input)
        {
            if (character == '\u001b')
            {
                FlushText();
                _sequence.Clear();
                _state = ParserState.Escape;
                continue;
            }
            if (character is '\u0018' or '\u001a')
            {
                FlushText();
                _sequence.Clear();
                _state = ParserState.Ground;
                continue;
            }

            switch (_state)
            {
                case ParserState.Ground:
                    if (character < ' ' || character == '\u007f')
                    {
                        FlushText();
                        ExecuteControl(character);
                    }
                    else if (character == '\u009b')
                    {
                        FlushText();
                        StartCsi();
                    }
                    else if (character == '\u009d')
                    {
                        FlushText();
                        StartOsc();
                    }
                    else
                    {
                        _text.Append(character);
                    }
                    break;
                case ParserState.Escape:
                    HandleEscape(character);
                    break;
                case ParserState.Csi:
                    HandleCsiCharacter(character);
                    break;
                case ParserState.IgnoreCsi:
                    if (character is >= '\u0040' and <= '\u007e')
                    {
                        _state = ParserState.Ground;
                    }
                    break;
                case ParserState.Osc:
                    if (character == '\a')
                    {
                        CompleteOsc();
                    }
                    else if (character < ' ' && character != '\t')
                    {
                        ExecuteControl(character);
                    }
                    else
                    {
                        AppendOsc(character);
                    }
                    break;
                case ParserState.OscEscape:
                    if (character == '\\')
                    {
                        CompleteOsc();
                    }
                    else
                    {
                        AppendOsc('\u001b');
                        AppendOsc(character);
                        if (_state == ParserState.OscEscape)
                        {
                            _state = ParserState.Osc;
                        }
                    }
                    break;
                case ParserState.IgnoreString:
                    if (character == '\a')
                    {
                        _state = ParserState.Ground;
                    }
                    else
                    {
                        _state = ParserState.IgnoreStringEscape;
                    }
                    break;
                case ParserState.IgnoreStringEscape:
                    _state = character == '\\' ? ParserState.Ground : ParserState.IgnoreString;
                    break;
                case ParserState.IgnoreOne:
                    _state = ParserState.Ground;
                    break;
            }
        }
        FlushText(flush: false);
    }

    internal void Complete()
    {
        FlushText();
        _sequence.Clear();
        _state = ParserState.Ground;
    }

    private void HandleEscape(char character)
    {
        switch (character)
        {
            case '[':
                StartCsi();
                break;
            case ']':
                StartOsc();
                break;
            case 'P':
            case 'X':
            case '^':
            case '_':
                _sequence.Clear();
                _state = ParserState.IgnoreString;
                _diagnostics.AddIgnoredSequence();
                break;
            case '7':
                _screen.SaveCursor();
                _state = ParserState.Ground;
                break;
            case '8':
                _screen.RestoreCursor();
                _state = ParserState.Ground;
                break;
            case 'D':
                _screen.LineFeed();
                _state = ParserState.Ground;
                break;
            case 'M':
                _screen.ReverseLineFeed();
                _state = ParserState.Ground;
                break;
            case 'E':
                _screen.CarriageReturn();
                _screen.LineFeed();
                _state = ParserState.Ground;
                break;
            case 'c':
                _screen.Reset();
                _style = TerminalStyle.Default;
                _state = ParserState.Ground;
                break;
            case '(':
            case ')':
            case '*':
            case '+':
            case 'N':
            case 'O':
            case '#':
                _state = ParserState.IgnoreOne;
                break;
            default:
                _diagnostics.AddIgnoredSequence();
                _state = ParserState.Ground;
                break;
        }
    }

    private void ExecuteControl(char character)
    {
        switch (character)
        {
            case '\b':
                _screen.Backspace();
                break;
            case '\t':
                _screen.Tab();
                break;
            case '\n':
            case '\v':
            case '\f':
                _screen.LineFeed();
                break;
            case '\r':
                _screen.CarriageReturn();
                break;
        }
    }

    private void FlushText(bool flush = true)
    {
        if (_text.Length == 0 && !flush)
        {
            return;
        }
        var text = _text.ToString();
        _text.Clear();
        _textElements.Write(text, flush, element =>
        {
            foreach (var rune in element.EnumerateRunes())
            {
                if (rune.Value == 0xFFFD)
                {
                    _diagnostics.AddReplacement();
                }
            }
            _screen.WriteTextElement(element, TerminalWidthPolicy.GetColumnWidth(element), _style);
        });
    }

    private void StartCsi()
    {
        _sequence.Clear();
        _state = ParserState.Csi;
    }

    private void StartOsc()
    {
        _sequence.Clear();
        _state = ParserState.Osc;
    }

    private void AppendOsc(char character)
    {
        if (_sequence.Length >= _options.MaxOscLength)
        {
            _diagnostics.AddTruncatedSequence();
            _state = ParserState.IgnoreString;
            return;
        }
        _sequence.Append(character);
    }

    private void CompleteOsc()
    {
        var sequence = _sequence.ToString();
        _sequence.Clear();
        _state = ParserState.Ground;
        _diagnostics.AddOsc();
        var separator = sequence.IndexOf(';');
        if (separator > 0 && sequence.AsSpan(0, separator) is "0" or "2")
        {
            _screen.SetTitle(sequence[(separator + 1)..]);
        }
        else
        {
            _diagnostics.AddIgnoredSequence();
        }
    }

    private void HandleCsiCharacter(char character)
    {
        if (character < ' ' && character != '\u007f')
        {
            ExecuteControl(character);
            return;
        }
        if (character is >= '\u0040' and <= '\u007e')
        {
            var sequence = _sequence.ToString();
            _sequence.Clear();
            _state = ParserState.Ground;
            HandleCsi(sequence, character);
            return;
        }
        if (character is not (>= '\u0020' and <= '\u003f'))
        {
            _diagnostics.AddIgnoredSequence();
            _state = ParserState.Ground;
            return;
        }
        if (_sequence.Length >= _options.MaxCsiLength)
        {
            _diagnostics.AddTruncatedSequence();
            _state = ParserState.IgnoreCsi;
            return;
        }
        _sequence.Append(character);
    }

    private void HandleCsi(string sequence, char final)
    {
        _diagnostics.AddCsi();
        var privatePrefix = sequence.Length > 0 && sequence[0] is '<' or '=' or '>' or '?' ? sequence[0] : '\0';
        var body = privatePrefix == '\0' ? sequence : sequence[1..];
        var intermediateIndex = body.IndexOfAny([' ', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/']);
        var intermediate = intermediateIndex >= 0 ? body[intermediateIndex..] : string.Empty;
        if (intermediateIndex >= 0)
        {
            body = body[..intermediateIndex];
        }
        var parameters = ParseParameters(body);
        if (parameters is null)
        {
            _diagnostics.AddTruncatedSequence();
            return;
        }

        if (privatePrefix == '?')
        {
            HandlePrivateMode(parameters, final);
            return;
        }
        if (privatePrefix != '\0' || intermediate.Length > 0)
        {
            if (final == 'c' && privatePrefix == '>')
            {
                Respond("\u001b[>0;0;0c");
            }
            else
            {
                _diagnostics.AddIgnoredSequence();
            }
            return;
        }

        var count = Parameter(parameters, 0, 1);
        switch (final)
        {
            case 'A': _screen.MoveCursor(-count, 0); break;
            case 'B': _screen.MoveCursor(count, 0); break;
            case 'C': _screen.MoveCursor(0, count); break;
            case 'D': _screen.MoveCursor(0, -count); break;
            case 'E': _screen.SetCursorColumn(0); _screen.MoveCursor(count, 0); break;
            case 'F': _screen.SetCursorColumn(0); _screen.MoveCursor(-count, 0); break;
            case 'G': _screen.SetCursorColumn(Parameter(parameters, 0, 1) - 1); break;
            case 'H':
            case 'f':
                _screen.SetCursor(Parameter(parameters, 1, 1) - 1, Parameter(parameters, 0, 1) - 1);
                break;
            case 'd': _screen.SetCursorRow(Parameter(parameters, 0, 1) - 1); break;
            case 'e': _screen.MoveCursor(count, 0); break;
            case 'a': _screen.MoveCursor(0, count); break;
            case 'J': _screen.EraseDisplay(Parameter(parameters, 0, 0), _style); break;
            case 'K': _screen.EraseLine(Parameter(parameters, 0, 0), _style); break;
            case 'L': _screen.InsertLines(count); break;
            case 'M': _screen.DeleteLines(count); break;
            case 'S': _screen.ScrollUp(count); break;
            case 'T': _screen.ScrollDown(count); break;
            case '@': _screen.InsertCharacters(count); break;
            case 'P': _screen.DeleteCharacters(count); break;
            case 'X': _screen.EraseCharacters(count, _style); break;
            case 'm': HandleSgr(parameters); break;
            case 'r': _screen.SetScrollRegion(Parameter(parameters, 0, 1) - 1, Parameter(parameters, 1, int.MaxValue) - 1); break;
            case 's': _screen.SaveCursor(); break;
            case 'u': _screen.RestoreCursor(); break;
            case 'h':
            case 'l':
                foreach (var parameter in parameters)
                {
                    _screen.SetMode(parameter, final == 'h');
                }
                break;
            case 'c':
                if (parameters.All(parameter => parameter is 0 or -1))
                {
                    Respond("\u001b[?1;2c");
                }
                break;
            case 'n':
                HandleDeviceStatus(parameters);
                break;
            default:
                _diagnostics.AddIgnoredSequence();
                break;
        }
    }

    private List<int>? ParseParameters(string body)
    {
        if (body.Length == 0)
        {
            return [-1];
        }
        var parts = body.Split(';');
        if (parts.Length > _options.MaxCsiParameters)
        {
            return null;
        }
        var parameters = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                parameters.Add(-1);
            }
            else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                parameters.Add(Math.Min(value, 1_000_000));
            }
            else
            {
                parameters.Add(-1);
            }
        }
        return parameters;
    }

    private void HandlePrivateMode(List<int> parameters, char final)
    {
        if (final is not ('h' or 'l'))
        {
            _diagnostics.AddIgnoredSequence();
            return;
        }
        var enabled = final == 'h';
        foreach (var parameter in parameters)
        {
            switch (parameter)
            {
                case 1:
                    _screen.SetApplicationCursorKeys(enabled);
                    break;
                case 47:
                    _screen.SetAlternateScreen(enabled, clear: false, saveCursor: false);
                    break;
                case 1047:
                    _screen.SetAlternateScreen(enabled, clear: enabled, saveCursor: false);
                    break;
                case 1048:
                    if (enabled) _screen.SaveCursor(); else _screen.RestoreCursor();
                    break;
                case 1049:
                    _screen.SetAlternateScreen(enabled, clear: enabled, saveCursor: true);
                    break;
                case 6:
                case 7:
                case 25:
                case 2004:
                case 9001:
                    _screen.SetMode(parameter, enabled);
                    break;
                default:
                    _diagnostics.AddIgnoredSequence();
                    break;
            }
        }
    }

    private void HandleSgr(List<int> parameters)
    {
        if (parameters.Count == 1 && parameters[0] == -1)
        {
            _style = TerminalStyle.Default;
            return;
        }

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index] < 0 ? 0 : parameters[index];
            switch (parameter)
            {
                case 0: _style = TerminalStyle.Default; break;
                case 1: _style = _style with { Bold = true }; break;
                case 22: _style = _style with { Bold = false }; break;
                case 4: _style = _style with { Underline = true }; break;
                case 24: _style = _style with { Underline = false }; break;
                case 7: _style = _style with { Inverse = true }; break;
                case 27: _style = _style with { Inverse = false }; break;
                case 39: _style = _style with { Foreground = TerminalColor.Default }; break;
                case 49: _style = _style with { Background = TerminalColor.Default }; break;
                case >= 30 and <= 37: _style = _style with { Foreground = TerminalColor.FromIndexed((byte)(parameter - 30)) }; break;
                case >= 40 and <= 47: _style = _style with { Background = TerminalColor.FromIndexed((byte)(parameter - 40)) }; break;
                case >= 90 and <= 97: _style = _style with { Foreground = TerminalColor.FromIndexed((byte)(parameter - 90 + 8)) }; break;
                case >= 100 and <= 107: _style = _style with { Background = TerminalColor.FromIndexed((byte)(parameter - 100 + 8)) }; break;
                case 38:
                case 48:
                    if (TryExtendedColor(parameters, ref index, out var color))
                    {
                        _style = parameter == 38 ? _style with { Foreground = color } : _style with { Background = color };
                    }
                    break;
                default:
                    break;
            }
        }
    }

    private static bool TryExtendedColor(List<int> parameters, ref int index, out TerminalColor color)
    {
        color = TerminalColor.Default;
        if (index + 1 >= parameters.Count)
        {
            return false;
        }
        var type = parameters[++index];
        if (type == 5 && index + 1 < parameters.Count)
        {
            var value = parameters[++index];
            if (value is >= 0 and <= 255)
            {
                color = TerminalColor.FromIndexed((byte)value);
                return true;
            }
        }
        else if (type == 2 && index + 3 < parameters.Count)
        {
            var red = parameters[++index];
            var green = parameters[++index];
            var blue = parameters[++index];
            if (red is >= 0 and <= 255 && green is >= 0 and <= 255 && blue is >= 0 and <= 255)
            {
                color = TerminalColor.FromRgb((byte)red, (byte)green, (byte)blue);
                return true;
            }
        }
        return false;
    }

    private void HandleDeviceStatus(List<int> parameters)
    {
        if (parameters[0] == 5)
        {
            Respond("\u001b[0n");
        }
        else if (parameters[0] == 6)
        {
            var snapshot = _screen.GetSnapshot(Guid.Empty);
            _respond(System.Text.Encoding.ASCII.GetBytes($"\u001b[{snapshot.CursorRow + 1};{snapshot.CursorColumn + 1}R"));
        }
    }

    private void Respond(string response) => _respond(System.Text.Encoding.ASCII.GetBytes(response));

    private static int Parameter(IReadOnlyList<int> parameters, int index, int defaultValue)
    {
        if (index >= parameters.Count || parameters[index] < 0)
        {
            return defaultValue;
        }
        return parameters[index] == 0 && defaultValue == 1 ? 1 : parameters[index];
    }
}
