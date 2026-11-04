using System.Numerics;
using ArkTerminal.Search;
using ArkTerminal.Selection;
using ArkTerminal.Terminal.Rendering;
using ArkTerminal.Terminal.Screen;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ArkTerminal.Rendering;

public sealed class DirectWriteTerminalRenderer : IDisposable
{
    private static readonly uint[] IndexedColors =
    [
        0xFF000000, 0xFFCD3131, 0xFF0DBC79, 0xFFE5E510, 0xFF2472C8, 0xFFBC3FBC, 0xFF11A8CD, 0xFFE5E5E5,
        0xFF666666, 0xFFF14C4C, 0xFF23D18B, 0xFFF5F543, 0xFF3B8EEA, 0xFFD670D6, 0xFF29B8DB, 0xFFFFFFFF
    ];

    private readonly TerminalViewLimits _limits;
    private readonly Dictionary<BrushKey, LinkedListNode<BrushEntry>> _brushes = new();
    private readonly LinkedList<BrushEntry> _lru = new();
    private ID2D1RenderTarget? _target;
    private ID2D1SolidColorBrush? _backgroundBrush;
    private ID2D1SolidColorBrush? _selectionBrush;
    private ID2D1SolidColorBrush? _searchBrush;
    private ID2D1SolidColorBrush? _cursorBrush;

    public DirectWriteTerminalRenderer(TerminalViewLimits limits) => _limits = limits;

    public int CachedBrushCount => _brushes.Count;

    public void Draw(
        ID2D1RenderTarget target,
        TerminalScreenSnapshot snapshot,
        TerminalFontService fonts,
        int startRow,
        int endRow,
        string composition,
        TerminalSelection? selection,
        IReadOnlyList<TerminalSearchMatch> searchMatches,
        TerminalVisualStyle visualStyle,
        Vector2? cursorGridPosition = null)
    {
        EnsureTarget(target);
        EnsureStyleBrushes(visualStyle);
        var fullFrame = startRow == 0 && endRow >= snapshot.Lines.Count - 1;
        if (fullFrame)
        {
            target.Clear(ToColor4(visualStyle.Background, visualStyle.BackgroundOpacity));
        }

        for (var row = startRow; row <= endRow && row < snapshot.Lines.Count; row++)
        {
            var y = (float)(row * fonts.LineHeight);
            var rowRect = new Rect(0, y, (float)(snapshot.Columns * fonts.CellWidth), (float)fonts.LineHeight);
            if (!fullFrame)
            {
                target.FillRectangle(rowRect, _backgroundBrush!);
            }
            var line = snapshot.Lines[row];
            var selected = selection?.GetColumnRange(line.LineId, snapshot.Columns);
            if (selected is not null)
            {
                target.FillRectangle(
                    new Rect((float)(selected.Value.Start * fonts.CellWidth), y, (float)((selected.Value.End - selected.Value.Start) * fonts.CellWidth), (float)fonts.LineHeight),
                    _selectionBrush!);
            }
            foreach (var match in searchMatches)
            {
                if (match.LineId == line.LineId)
                {
                    target.FillRectangle(
                        new Rect((float)(match.StartColumn * fonts.CellWidth), y, (float)((match.EndColumn - match.StartColumn) * fonts.CellWidth), (float)fonts.LineHeight),
                        _searchBrush!);
                }
            }
            DrawLine(target, line, fonts, y, visualStyle);
        }

        if (snapshot.CursorVisible)
        {
            var cursorX = (cursorGridPosition?.X ?? snapshot.CursorColumn) * (float)fonts.CellWidth;
            var cursorY = (cursorGridPosition?.Y ?? snapshot.CursorRow) * (float)fonts.LineHeight;
            target.DrawRectangle(new Rect(cursorX + 1, cursorY + 1, (float)Math.Max(3, fonts.CellWidth - 2), (float)(fonts.LineHeight - 2)), _cursorBrush!, 1);
        }

        if (composition.Length > 0 && snapshot.CursorVisible && snapshot.CursorRow >= 0 && snapshot.CursorRow < snapshot.Rows)
        {
            var columns = Math.Max(1, GetTextColumns(composition));
            var layout = fonts.GetLayout(composition, false, columns);
            var defaultForeground = TerminalColor.FromRgb(visualStyle.Foreground.R, visualStyle.Foreground.G, visualStyle.Foreground.B);
            var brush = GetBrush(target, TerminalColor.Default, defaultForeground);
            var origin = SnapToPixel(
                target,
                new Vector2(
                    (float)(snapshot.CursorColumn * fonts.CellWidth),
                    (float)(snapshot.CursorRow * fonts.LineHeight)));
            target.DrawTextLayout(origin, layout, brush, DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
            target.DrawLine(
                origin with { Y = origin.Y + (float)fonts.LineHeight - 2 },
                origin with { X = origin.X + (float)(columns * fonts.CellWidth), Y = origin.Y + (float)fonts.LineHeight - 2 },
                brush,
                1);
        }
    }

    private static int GetTextColumns(string text)
    {
        var columns = 0;
        var offset = 0;
        while (offset < text.Length)
        {
            var length = System.Globalization.StringInfo.GetNextTextElementLength(text, offset);
            columns += TerminalWidthPolicy.GetColumnWidth(text.Substring(offset, length));
            offset += length;
        }
        return columns;
    }

    public void Dispose()
    {
        ClearBrushes();
    }

    private void DrawLine(ID2D1RenderTarget target, TerminalLineSnapshot line, TerminalFontService fonts, float y, TerminalVisualStyle visualStyle)
    {
        var groups = (fonts.Options.Ligatures ? CreateLigatureGroups(line.Clusters) : CreateClusterGroups(line.Clusters)).ToArray();
        var defaultBackground = TerminalColor.FromRgb(visualStyle.Background.R, visualStyle.Background.G, visualStyle.Background.B);
        var defaultForeground = TerminalColor.FromRgb(visualStyle.Foreground.R, visualStyle.Foreground.G, visualStyle.Foreground.B);

        // Adjacent cells commonly repeat one SGR background. Fill each color run once so
        // fractional cell widths cannot leave antialiased vertical seams in selected rows.
        for (var index = 0; index < groups.Length;)
        {
            var group = groups[index];
            var background = EffectiveBackground(group);
            if (background.Kind == TerminalColorKind.Default)
            {
                index++;
                continue;
            }
            var start = group.StartColumn;
            var end = start + group.Columns;
            var next = index + 1;
            while (next < groups.Length && groups[next].StartColumn == end && EffectiveBackground(groups[next]) == background)
            {
                end += groups[next].Columns;
                next++;
            }
            var rect = new Rect((float)(start * fonts.CellWidth), y, (float)((end - start) * fonts.CellWidth), (float)fonts.LineHeight);
            FillRectangleAliased(target, rect, GetBrush(target, background, defaultBackground));
            index = next;
        }

        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Text)) continue;
            var foreground = group.Inverse ? group.Background : group.Foreground;
            var x = (float)(group.StartColumn * fonts.CellWidth);
            var width = (float)(group.Columns * fonts.CellWidth);
            var rect = new Rect(x, y, width, (float)fonts.LineHeight);
            var brush = GetBrush(target, foreground, defaultForeground);
            if (!TerminalGridGlyphRenderer.TryDraw(target, group.Text, rect, brush))
            {
                var layout = fonts.GetLayout(group.Text, group.Bold, group.Columns);
                target.PushAxisAlignedClip(rect, AntialiasMode.PerPrimitive);
                target.DrawTextLayout(
                    SnapToPixel(target, new Vector2(x, y)),
                    layout,
                    brush,
                    DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
                target.PopAxisAlignedClip();
            }
            if (group.Underline)
            {
                target.DrawLine(new Vector2(x, y + (float)fonts.LineHeight - 2), new Vector2(x + width, y + (float)fonts.LineHeight - 2), brush, 1);
            }
        }
    }

    private static TerminalColor EffectiveBackground(TextGroup group) => group.Inverse ? group.Foreground : group.Background;

    private static void FillRectangleAliased(ID2D1RenderTarget target, Rect rect, ID2D1SolidColorBrush brush)
    {
        var previousMode = target.AntialiasMode;
        target.AntialiasMode = AntialiasMode.Aliased;
        try
        {
            target.FillRectangle(TerminalGridGlyphRenderer.SnapToDevicePixels(target, rect), brush);
        }
        finally
        {
            target.AntialiasMode = previousMode;
        }
    }

    private static IEnumerable<TextGroup> CreateClusterGroups(IReadOnlyList<TerminalClusterSnapshot> clusters)
    {
        foreach (var cluster in clusters)
        {
            yield return new TextGroup(cluster, cluster.StartColumn, cluster.ColumnWidth, cluster.Text);
        }
    }

    private static IEnumerable<TextGroup> CreateLigatureGroups(IReadOnlyList<TerminalClusterSnapshot> clusters)
    {
        for (var index = 0; index < clusters.Count;)
        {
            var first = clusters[index];
            if (!IsLigatureCandidate(first))
            {
                yield return new TextGroup(first, first.StartColumn, first.ColumnWidth, first.Text);
                index++;
                continue;
            }

            var end = index + 1;
            var text = new System.Text.StringBuilder(first.Text);
            while (end < clusters.Count && IsLigatureCandidate(clusters[end]) && SameStyle(first, clusters[end]))
            {
                text.Append(clusters[end].Text);
                end++;
            }
            var columns = clusters[end - 1].StartColumn + clusters[end - 1].ColumnWidth - first.StartColumn;
            yield return new TextGroup(first, first.StartColumn, columns, text.ToString());
            index = end;
        }
    }

    private static bool IsLigatureCandidate(TerminalClusterSnapshot cluster) =>
        cluster.IsOccupied && cluster.ColumnWidth == 1 && cluster.Text.Length == 1 && cluster.Text[0] is >= '!' and <= '~';

    private static bool SameStyle(TerminalClusterSnapshot left, TerminalClusterSnapshot right) =>
        left.Foreground == right.Foreground && left.Background == right.Background && left.Bold == right.Bold &&
        left.Underline == right.Underline && left.Inverse == right.Inverse;

    private void EnsureTarget(ID2D1RenderTarget target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }
        ClearBrushes();
        _target = target;
        // The shared texture has an alpha channel; grayscale avoids ClearType color fringes
        // and matches the glyph appearance used by native terminal renderers such as otty.
        target.TextAntialiasMode = TextAntialiasMode.Grayscale;
    }

    private static Vector2 SnapToPixel(ID2D1RenderTarget target, Vector2 origin)
    {
        var scale = target.Dpi.Width / 96f;
        if (scale <= 0)
        {
            return origin;
        }
        return new Vector2(
            MathF.Round(origin.X * scale) / scale,
            MathF.Round(origin.Y * scale) / scale);
    }

    private void EnsureStyleBrushes(TerminalVisualStyle style)
    {
        if (_backgroundBrush is not null)
        {
            return;
        }
        _backgroundBrush = _target!.CreateSolidColorBrush(ToColor4(style.Background, style.BackgroundOpacity));
        _selectionBrush = _target.CreateSolidColorBrush(ToColor4(style.Selection, 1));
        _searchBrush = _target.CreateSolidColorBrush(ToColor4(style.Search, 1));
        _cursorBrush = _target.CreateSolidColorBrush(ToColor4(style.Cursor, 1));
    }

    private static Color4 ToColor4(System.Windows.Media.Color color, double opacity) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        (float)(color.A / 255d * opacity));

    private ID2D1SolidColorBrush GetBrush(ID2D1RenderTarget target, TerminalColor color, TerminalColor defaultColor)
    {
        var effective = color.Kind == TerminalColorKind.Default ? defaultColor : color;
        var key = new BrushKey(effective);
        if (_brushes.TryGetValue(key, out var existing))
        {
            _lru.Remove(existing);
            _lru.AddFirst(existing);
            return existing.Value.Brush;
        }

        var argb = effective.Kind switch
        {
            TerminalColorKind.Indexed => IndexedColors[effective.Red],
            TerminalColorKind.Rgb => 0xFF000000u | ((uint)effective.Red << 16) | ((uint)effective.Green << 8) | effective.Blue,
            _ => 0xFFDCDCDCu
        };
        var brush = target.CreateSolidColorBrush(new Color4(
            ((argb >> 16) & 0xFF) / 255f,
            ((argb >> 8) & 0xFF) / 255f,
            (argb & 0xFF) / 255f,
            1));
        var node = new LinkedListNode<BrushEntry>(new BrushEntry(key, brush));
        _brushes.Add(key, node);
        _lru.AddFirst(node);
        while (_brushes.Count > 18 + _limits.MaxTrueColorBrushes)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _brushes.Remove(last.Value.Key);
            last.Value.Brush.Dispose();
        }
        return brush;
    }

    private void ClearBrushes()
    {
        foreach (var entry in _brushes.Values)
        {
            entry.Value.Brush.Dispose();
        }
        _brushes.Clear();
        _lru.Clear();
        _backgroundBrush?.Dispose();
        _selectionBrush?.Dispose();
        _searchBrush?.Dispose();
        _cursorBrush?.Dispose();
        _backgroundBrush = null;
        _selectionBrush = null;
        _searchBrush = null;
        _cursorBrush = null;
        _target = null;
    }

    private sealed record TextGroup(TerminalClusterSnapshot Style, int StartColumn, int Columns, string Text)
    {
        internal TerminalColor Foreground => Style.Foreground;
        internal TerminalColor Background => Style.Background;
        internal bool Bold => Style.Bold;
        internal bool Underline => Style.Underline;
        internal bool Inverse => Style.Inverse;
    }

    private readonly record struct BrushKey(TerminalColor Color);
    private sealed record BrushEntry(BrushKey Key, ID2D1SolidColorBrush Brush);
}
