using System.Globalization;

namespace ArkTerminal.Terminal.Screen;

internal sealed class IncrementalTextElementSegmenter
{
    private const int MaxPendingUtf16Length = 256;
    private static readonly string[] AsciiElements = Enumerable.Range(0, 128).Select(value => ((char)value).ToString()).ToArray();
    private string _pending = string.Empty;
    private int _publishedLength;

    internal void Write(string text, bool flush, Action<string> emit, Action<string, string> update)
    {
        _pending += text;
        var offset = 0;
        while (offset < _pending.Length)
        {
            var length = StringInfo.GetNextTextElementLength(_pending, offset);
            var isLast = offset + length == _pending.Length;
            var element = length == 1 && _pending[offset] < AsciiElements.Length
                ? AsciiElements[_pending[offset]] : _pending.Substring(offset, length);
            if (_publishedLength > 0)
            {
                if (length != _publishedLength) update(_pending[.._publishedLength], element);
                _publishedLength = 0;
            }
            else
            {
                emit(element);
            }
            if (!flush && isLast && length <= MaxPendingUtf16Length)
            {
                // Display the tail now, but retain it so later code points update the same cluster.
                _publishedLength = length;
                break;
            }
            offset += length;
        }
        _pending = offset == 0 ? _pending : _pending[offset..];
        if (flush)
        {
            _pending = string.Empty;
        }
    }
}
