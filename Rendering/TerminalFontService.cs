using Vortice.DirectWrite;

namespace ArkTerminal.Rendering;

public sealed class TerminalFontService : IDisposable
{
    private static readonly string[] MonospaceFallbacks =
    [
        "Cascadia Mono", "Cascadia Code", "Consolas", "JetBrainsMono Nerd Font Mono",
        "JetBrains Mono", "DejaVu Sans Mono", "Courier New"
    ];

    private readonly TerminalViewLimits _limits;
    private readonly IDWriteFactory _factory;
    private readonly IDWriteFontCollection _fontCollection;
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
        _fontCollection = _factory.GetSystemFontCollection(true);
        ResolvedFontFamily = ResolveFontFamily(options.FontFamily);
        IsFallback = !string.Equals(ResolvedFontFamily, options.FontFamily, StringComparison.OrdinalIgnoreCase);
        _regular = CreateFormat(ResolvedFontFamily, options.FontSize, (FontWeight)options.FontWeight);
        _bold = CreateFormat(ResolvedFontFamily, options.FontSize, FontWeight.Bold);
        using var measurement = _factory.CreateTextLayout("0", _regular, float.MaxValue, float.MaxValue);
        var metrics = measurement.Metrics;
        CellWidth = Math.Max(1, MeasureAdvance(options.FontSize, metrics.WidthIncludingTrailingWhitespace) + options.CharacterSpacing);
        LineHeight = Math.Max(1, Math.Ceiling(metrics.Height * options.LineHeightMultiplier));
    }

    public TerminalFontOptions Options { get; }
    public string ResolvedFontFamily { get; }
    public bool IsFallback { get; }
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
        _fontCollection.Dispose();
        _factory.Dispose();
        _disposed = true;
    }

    private IDWriteTextFormat CreateFormat(string family, double size, FontWeight weight)
    {
        var format = _factory.CreateTextFormat(
            family,
            _fontCollection,
            weight,
            FontStyle.Normal,
            FontStretch.Normal,
            (float)size,
            "zh-CN");
        format.WordWrapping = WordWrapping.NoWrap;
        format.TextAlignment = TextAlignment.Leading;
        format.ParagraphAlignment = ParagraphAlignment.Near;
        return format;
    }

    private string ResolveFontFamily(string requested)
    {
        var candidates = new[] { requested }.Concat(MonospaceFallbacks);
        foreach (var candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            if (_fontCollection.FindFamilyName(candidate, out _))
            {
                return candidate;
            }
        }
        return "Segoe UI";
    }

    private double MeasureAdvance(double fontSize, double layoutFallback)
    {
        if (!_fontCollection.FindFamilyName(ResolvedFontFamily, out var familyIndex))
        {
            return layoutFallback;
        }
        using var family = _fontCollection.GetFontFamily(familyIndex);
        using var font = family.GetFirstMatchingFont((FontWeight)Options.FontWeight, FontStretch.Normal, FontStyle.Normal);
        using var face = font.CreateFontFace();
        var metrics = face.Metrics;
        var glyph = face.GetGlyphIndices(new uint[] { '0' })[0];
        if (glyph == 0 || metrics.DesignUnitsPerEm == 0)
        {
            return layoutFallback;
        }
        var glyphMetrics = face.GetDesignGlyphMetrics(new ushort[] { glyph }, false)[0];
        var advance = glyphMetrics.AdvanceWidth * fontSize / metrics.DesignUnitsPerEm;
        return advance > 0 ? advance : layoutFallback;
    }

    private readonly record struct LayoutKey(string Text, bool Bold, int Columns);
    private sealed record CacheEntry(LayoutKey Key, IDWriteTextLayout Layout);
}
