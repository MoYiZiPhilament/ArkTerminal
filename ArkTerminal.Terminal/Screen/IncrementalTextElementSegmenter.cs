using System.Globalization;

namespace ArkTerminal.Terminal.Screen;

internal sealed class IncrementalTextElementSegmenter
{
    private const int MaxPendingUtf16Length = 256;
    private string _pending = string.Empty;

    internal void Write(string text, bool flush, Action<string> emit)
    {
        _pending += text;
        var offset = 0;
        while (offset < _pending.Length)
        {
            var length = StringInfo.GetNextTextElementLength(_pending, offset);
            var isLast = offset + length == _pending.Length;
            if (!flush && isLast && _pending.Length <= MaxPendingUtf16Length)
            {
                break;
            }
            emit(_pending.Substring(offset, length));
            offset += length;
        }
        _pending = offset == 0 ? _pending : _pending[offset..];
        if (flush)
        {
            _pending = string.Empty;
        }
    }
}
