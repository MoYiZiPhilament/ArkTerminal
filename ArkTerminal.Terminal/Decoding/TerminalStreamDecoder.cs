using System.Text;
using ArkTerminal.Terminal.Screen;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Vt;

namespace ArkTerminal.Terminal.Decoding;

public sealed class TerminalStreamDecoder
{
    private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private readonly VtParser _parser;
    private readonly TerminalDiagnostics _diagnostics = new();
    private readonly char[] _characters;

    public TerminalStreamDecoder(
        TerminalScreen screen,
        TerminalSessionOptions options,
        Action<ReadOnlyMemory<byte>>? respond = null,
        Action<string>? osc = null,
        Action<string>? text = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _characters = new char[options.ReadBufferSize * 2 + 8];
        _parser = new VtParser(screen, options, _diagnostics, response => respond?.Invoke(response), osc, text);
    }

    public TerminalDiagnosticSnapshot Diagnostics => _diagnostics.Snapshot();

    public void Write(byte[] bytes, int count)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (count < 0 || count > bytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
        var offset = 0;
        while (offset < count)
        {
            _decoder.Convert(
                bytes,
                offset,
                count - offset,
                _characters,
                0,
                _characters.Length,
                false,
                out var bytesUsed,
                out var charactersUsed,
                out _);
            if (bytesUsed == 0 && charactersUsed == 0)
            {
                throw new InvalidOperationException("UTF-8 解码器未能推进输入。");
            }
            offset += bytesUsed;
            if (charactersUsed > 0)
            {
                _parser.Write(_characters.AsSpan(0, charactersUsed));
            }
        }
    }

    public void Complete()
    {
        _decoder.Convert(
            Array.Empty<byte>(),
            0,
            0,
            _characters,
            0,
            _characters.Length,
            true,
            out _,
            out var charactersUsed,
            out _);
        if (charactersUsed > 0)
        {
            _parser.Write(_characters.AsSpan(0, charactersUsed));
        }
        _parser.Complete();
    }
}
