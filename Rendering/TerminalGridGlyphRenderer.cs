using System.Numerics;
using System.Text;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ArkTerminal.Rendering;

internal static class TerminalGridGlyphRenderer
{
    private static readonly int[] Bayer4 =
    [
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5
    ];

    public static bool TryDraw(ID2D1RenderTarget target, string text, Rect rect, ID2D1SolidColorBrush brush)
    {
        if (text.Length == 0 || text.Length > 2)
        {
            return false;
        }
        var rune = Rune.GetRuneAt(text, 0);
        if (rune.Utf16SequenceLength != text.Length)
        {
            return false;
        }
        var pixelRect = SnapToDevicePixels(target, rect);
        if (rune.Value is >= 0x2580 and <= 0x259F)
        {
            return TryDrawBlockElement(target, (char)rune.Value, pixelRect, brush);
        }
        if (rune.Value is >= 0x2500 and <= 0x257F)
        {
            return TerminalBoxDrawingRenderer.TryDraw(target, (char)rune.Value, pixelRect, brush);
        }
        if (rune.Value is >= 0x2800 and <= 0x28FF)
        {
            DrawBraille(target, rune.Value & 0xFF, pixelRect, brush);
            return true;
        }
        if (rune.Value is >= 0xE0B0 and <= 0xE0CF)
        {
            return TryDrawPowerline(target, (char)rune.Value, pixelRect, brush);
        }
        if (rune.Value is >= 0x1FB00 and <= 0x1FBFF)
        {
            return TerminalLegacyGridGlyphRenderer.TryDraw(target, rune, pixelRect, brush);
        }
        if (rune.Value is >= 0x1CD00 and <= 0x1CDE5)
        {
            return TerminalOctantGridGlyphRenderer.TryDraw(target, rune, pixelRect, brush);
        }
        return false;
    }

    public static Rect SnapToDevicePixels(ID2D1RenderTarget target, Rect rect)
    {
        var scale = target.Dpi.Width / 96f;
        if (scale <= 0)
        {
            return rect;
        }
        var left = MathF.Round(rect.X * scale) / scale;
        var top = MathF.Round(rect.Y * scale) / scale;
        var right = MathF.Round((rect.X + rect.Width) * scale) / scale;
        var bottom = MathF.Round((rect.Y + rect.Height) * scale) / scale;
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static bool TryDrawBlockElement(ID2D1RenderTarget target, char value, Rect rect, ID2D1SolidColorBrush brush)
    {
        var x = rect.X;
        var y = rect.Y;
        var width = rect.Width;
        var height = rect.Height;
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        var eighthWidth = width / 8;
        var eighthHeight = height / 8;
        var previousMode = target.AntialiasMode;
        target.AntialiasMode = AntialiasMode.Aliased;
        try
        {
            void Fill(float offsetX, float offsetY, float fillWidth, float fillHeight) =>
                target.FillRectangle(new Rect(x + offsetX, y + offsetY, fillWidth, fillHeight), brush);

            if (value is >= '\u2591' and <= '\u2593')
            {
                var coverage = value - '\u2591' + 1;
                var cellWidth = width / 4;
                var cellHeight = height / 4;
                for (var index = 0; index < Bayer4.Length; index++)
                {
                    if (Bayer4[index] >= coverage * 4) continue;
                    Fill(index % 4 * cellWidth, index / 4 * cellHeight, cellWidth, cellHeight);
                }
                return true;
            }

            switch (value)
            {
                case '\u2580': Fill(0, 0, width, halfHeight); break;
                case '\u2581': Fill(0, height - eighthHeight, width, eighthHeight); break;
                case '\u2582': Fill(0, height - 2 * eighthHeight, width, 2 * eighthHeight); break;
                case '\u2583': Fill(0, height - 3 * eighthHeight, width, 3 * eighthHeight); break;
                case '\u2584': Fill(0, halfHeight, width, halfHeight); break;
                case '\u2585': Fill(0, height - 5 * eighthHeight, width, 5 * eighthHeight); break;
                case '\u2586': Fill(0, height - 6 * eighthHeight, width, 6 * eighthHeight); break;
                case '\u2587': Fill(0, height - 7 * eighthHeight, width, 7 * eighthHeight); break;
                case '\u2588': Fill(0, 0, width, height); break;
                case '\u2589': Fill(0, 0, 7 * eighthWidth, height); break;
                case '\u258A': Fill(0, 0, 6 * eighthWidth, height); break;
                case '\u258B': Fill(0, 0, 5 * eighthWidth, height); break;
                case '\u258C': Fill(0, 0, halfWidth, height); break;
                case '\u258D': Fill(0, 0, 3 * eighthWidth, height); break;
                case '\u258E': Fill(0, 0, 2 * eighthWidth, height); break;
                case '\u258F': Fill(0, 0, eighthWidth, height); break;
                case '\u2590': Fill(halfWidth, 0, halfWidth, height); break;
                case '\u2594': Fill(0, 0, width, eighthHeight); break;
                case '\u2595': Fill(width - eighthWidth, 0, eighthWidth, height); break;
                case '\u2596': Fill(0, halfHeight, halfWidth, halfHeight); break;
                case '\u2597': Fill(halfWidth, halfHeight, halfWidth, halfHeight); break;
                case '\u2598': Fill(0, 0, halfWidth, halfHeight); break;
                case '\u2599':
                    Fill(0, 0, halfWidth, height);
                    Fill(halfWidth, halfHeight, halfWidth, halfHeight);
                    break;
                case '\u259A':
                    Fill(0, 0, halfWidth, halfHeight);
                    Fill(halfWidth, halfHeight, halfWidth, halfHeight);
                    break;
                case '\u259B':
                    Fill(0, 0, width, halfHeight);
                    Fill(0, halfHeight, halfWidth, halfHeight);
                    break;
                case '\u259C':
                    Fill(0, 0, width, halfHeight);
                    Fill(halfWidth, halfHeight, halfWidth, halfHeight);
                    break;
                case '\u259D': Fill(halfWidth, 0, halfWidth, halfHeight); break;
                case '\u259E':
                    Fill(halfWidth, 0, halfWidth, halfHeight);
                    Fill(0, halfHeight, halfWidth, halfHeight);
                    break;
                case '\u259F':
                    Fill(halfWidth, 0, halfWidth, halfHeight);
                    Fill(0, halfHeight, width, halfHeight);
                    break;
                default: return false;
            }
        }
        finally
        {
            target.AntialiasMode = previousMode;
        }
        return true;
    }

    private static void DrawBraille(ID2D1RenderTarget target, int mask, Rect rect, ID2D1SolidColorBrush brush)
    {
        if (mask == 0) return;
        var dotWidth = rect.Width / 2;
        var dotHeight = rect.Height / 4;
        var radius = Math.Max(0.5f, Math.Min(dotWidth, dotHeight) * 0.3f);
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var bit = row switch
                {
                    0 => column == 0 ? 0 : 3,
                    1 => column == 0 ? 1 : 4,
                    2 => column == 0 ? 2 : 5,
                    _ => column == 0 ? 6 : 7
                };
                if ((mask & (1 << bit)) == 0) continue;
                var center = new Vector2(
                    rect.X + (column + 0.5f) * dotWidth,
                    rect.Y + (row + 0.5f) * dotHeight);
                target.FillEllipse(new Ellipse(center, radius, radius), brush);
            }
        }
    }

    private static bool TryDrawPowerline(ID2D1RenderTarget target, char value, Rect rect, ID2D1SolidColorBrush brush)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        var centerY = (top + bottom) / 2;
        switch (value)
        {
            case '\uE0B0':
                FillPolygon(target, brush, new(left, top), new(right, centerY), new(left, bottom));
                return true;
            case '\uE0B2':
                FillPolygon(target, brush, new(right, top), new(left, centerY), new(right, bottom));
                return true;
            case '\uE0B8':
                FillPolygon(target, brush, new(left, top), new(left, bottom), new(right, bottom));
                return true;
            case '\uE0BA':
                FillPolygon(target, brush, new(right, top), new(left, bottom), new(right, bottom));
                return true;
            case '\uE0BC':
                FillPolygon(target, brush, new(left, top), new(right, top), new(left, bottom));
                return true;
            case '\uE0BE':
                FillPolygon(target, brush, new(left, top), new(right, top), new(right, bottom));
                return true;
            case '\uE0B1':
                DrawLines(target, brush, new(left, top), new(right, centerY), new(left, bottom));
                return true;
            case '\uE0B3':
                DrawLines(target, brush, new(right, top), new(left, centerY), new(right, bottom));
                return true;
            case '\uE0B9':
                DrawLines(target, brush, new(left, top), new(left, bottom), new(right, bottom));
                return true;
            case '\uE0BB':
                DrawLines(target, brush, new(right, top), new(right, bottom), new(left, bottom));
                return true;
            case '\uE0BD':
                DrawLines(target, brush, new(left, bottom), new(left, top), new(right, top));
                return true;
            case '\uE0BF':
                DrawLines(target, brush, new(left, top), new(right, top), new(right, bottom));
                return true;
            default:
                return false;
        }
    }

    private static void FillPolygon(ID2D1RenderTarget target, ID2D1SolidColorBrush brush, params Vector2[] points)
    {
        using var geometry = target.Factory.CreatePathGeometry();
        using var sink = geometry.Open();
        sink.BeginFigure(points[0], FigureBegin.Filled);
        for (var index = 1; index < points.Length; index++) sink.AddLine(points[index]);
        sink.EndFigure(FigureEnd.Closed);
        sink.Close();
        target.FillGeometry(geometry, brush);
    }

    private static void DrawLines(ID2D1RenderTarget target, ID2D1SolidColorBrush brush, params Vector2[] points)
    {
        var thickness = Math.Max(1, MathF.Abs(points[0].Y - points[^1].Y) / 18);
        for (var index = 1; index < points.Length; index++)
        {
            target.DrawLine(points[index - 1], points[index], brush, thickness);
        }
    }
}
