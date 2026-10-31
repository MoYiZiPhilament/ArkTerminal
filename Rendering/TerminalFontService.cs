using Vortice.DirectWrite;

namespace ArkTerminal.Rendering;

public sealed class TerminalFontService : IDisposable
{
    private readonly TerminalViewLimits _limits;
    private readonly IDWriteFactory _factory;
    private readonly IDWriteTextFormat _regular;
    private readonly IDWriteTextFormat _bold;
    private readonly Dictionary<LayoutKey, LinkedListNode<CacheEntry>> _layouts = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private bool _disposed;

    public TerminalFontService(TerminalFontOptions options, TerminalViewLimits limits)
    {
        Options = options;
        _limits = limits;
        _factory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _regular = CreateFormat(options.FontFamily, options.FontSize, (FontWeight)options.FontWeight);
        _bold = CreateFormat(options.FontFamily, options.FontSize, FontWeight.Bold);
        using var measurement = _factory.CreateTextLayout("0", _regular, float.MaxValue, float.MaxValue);
        var metrics = measurement.Metrics;
        CellWidth = Math.Max(1, metrics.WidthIncludingTrailingWhitespace + options.CharacterSpacing);
        LineHeight = Math.Max(1, Math.Ceiling(metrics.Height * options.LineHeightMultiplier));
    }

    public TerminalFontOptions Options { get; }
    public double CellWidth { get; }
    public double LineHeight { get; }
    public int CachedLayoutCount => _layouts.Count;

    public IDWriteTextLayout GetLayout(string text, bool bold, int columns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = new LayoutKey(text, bold, columns);
        if (_layouts.TryGetValue(key, out var existing))
        {
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return existing.Value.Layout;
        }

        var layout = _factory.CreateTextLayout(
            text,
            bold ? _bold : _regular,
            (float)Math.Max(1, columns * CellWidth),
            (float)LineHeight);
        var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, layout));
        _layouts.Add(key, node);
        _lru.AddFirst(node);
        while (_layouts.Count > _limits.MaxCachedLayouts)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _layouts.Remove(last.Value.Key);
            last.Value.Layout.Dispose();
        }
        return layout;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        foreach (var entry in _layouts.Values)
        {
            entry.Value.Layout.Dispose();
        }
        _layouts.Clear();
        _lru.Clear();
        _regular.Dispose();
        _bold.Dispose();
        _factory.Dispose();
        _disposed = true;
    }

    private IDWriteTextFormat CreateFormat(string family, double size, FontWeight weight)
    {
        var format = _factory.CreateTextFormat(
            family,
            null!,
            weight,
            FontStyle.Normal,
            FontStretch.Normal,
            (float)size,
            "zh-CN");
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }

    private readonly record struct LayoutKey(string Text, bool Bold, int Columns);
    private sealed record CacheEntry(LayoutKey Key, IDWriteTextLayout Layout);
}
