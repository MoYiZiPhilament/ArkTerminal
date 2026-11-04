using System.Numerics;
using System.Text;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ArkTerminal.Rendering;

/// <summary>
/// Draws the cell-defined Symbols for Legacy Computing glyphs without depending on font coverage.
/// Mappings follow the Unicode 16.0 NamesList.txt and UnicodeData.txt:
/// https://www.unicode.org/Public/16.0.0/ucd/NamesList.txt
/// https://www.unicode.org/Public/16.0.0/ucd/UnicodeData.txt
/// Shade patterns, diagonal fills, icons, circles, and segmented digits deliberately fall back to
/// the font because their exact cell geometry is not defined by their character names alone.
/// Block octants are in U+1CD00..U+1CDE5 and are outside this renderer's U+1FB00..U+1FBFF scope.
/// </summary>
internal static class TerminalLegacyGridGlyphRenderer
{
    internal static bool TryDraw(ID2D1RenderTarget target, Rune value, Rect rect, ID2D1SolidColorBrush brush)
    {
        var codePoint = value.Value;
        if ((uint)(codePoint - 0x1FB00) > 0xFF)
        {
            return false;
        }

        var previousMode = target.AntialiasMode;
        try
        {
            target.AntialiasMode = AntialiasMode.Aliased;
            var sextantMask = GetSextantMask(codePoint);
            if (sextantMask != 0)
            {
                DrawSextant(target, rect, brush, sextantMask);
                return true;
            }

            if (TryDrawBlock(target, codePoint, rect, brush))
            {
                return true;
            }

            target.AntialiasMode = AntialiasMode.PerPrimitive;
            var diagonal = GetDiagonalBlock(codePoint);
            if (diagonal.HasValue)
            {
                DrawDiagonalBlock(target, rect, brush, diagonal.Value);
                return true;
            }

            return TryDrawDiagonalLine(target, codePoint, rect, brush);
        }
        finally
        {
            target.AntialiasMode = previousMode;
        }
    }

    // The six cells are numbered by their official names: 1/2 upper, 3/4 middle,
    // 5/6 lower, with odd cells on the left. Each code point is mapped explicitly.
    private static byte GetSextantMask(int codePoint) => codePoint switch
    {
        0x1FB00 => 0x01, // BLOCK SEXTANT-1
        0x1FB01 => 0x02, // BLOCK SEXTANT-2
        0x1FB02 => 0x03, // BLOCK SEXTANT-12
        0x1FB03 => 0x04, // BLOCK SEXTANT-3
        0x1FB04 => 0x05, // BLOCK SEXTANT-13
        0x1FB05 => 0x06, // BLOCK SEXTANT-23
        0x1FB06 => 0x07, // BLOCK SEXTANT-123
        0x1FB07 => 0x08, // BLOCK SEXTANT-4
        0x1FB08 => 0x09, // BLOCK SEXTANT-14
        0x1FB09 => 0x0A, // BLOCK SEXTANT-24
        0x1FB0A => 0x0B, // BLOCK SEXTANT-124
        0x1FB0B => 0x0C, // BLOCK SEXTANT-34
        0x1FB0C => 0x0D, // BLOCK SEXTANT-134
        0x1FB0D => 0x0E, // BLOCK SEXTANT-234
        0x1FB0E => 0x0F, // BLOCK SEXTANT-1234
        0x1FB0F => 0x10, // BLOCK SEXTANT-5
        0x1FB10 => 0x11, // BLOCK SEXTANT-15
        0x1FB11 => 0x12, // BLOCK SEXTANT-25
        0x1FB12 => 0x13, // BLOCK SEXTANT-125
        0x1FB13 => 0x14, // BLOCK SEXTANT-35
        0x1FB14 => 0x16, // BLOCK SEXTANT-235
        0x1FB15 => 0x17, // BLOCK SEXTANT-1235
        0x1FB16 => 0x18, // BLOCK SEXTANT-45
        0x1FB17 => 0x19, // BLOCK SEXTANT-145
        0x1FB18 => 0x1A, // BLOCK SEXTANT-245
        0x1FB19 => 0x1B, // BLOCK SEXTANT-1245
        0x1FB1A => 0x1C, // BLOCK SEXTANT-345
        0x1FB1B => 0x1D, // BLOCK SEXTANT-1345
        0x1FB1C => 0x1E, // BLOCK SEXTANT-2345
        0x1FB1D => 0x1F, // BLOCK SEXTANT-12345
        0x1FB1E => 0x20, // BLOCK SEXTANT-6
        0x1FB1F => 0x21, // BLOCK SEXTANT-16
        0x1FB20 => 0x22, // BLOCK SEXTANT-26
        0x1FB21 => 0x23, // BLOCK SEXTANT-126
        0x1FB22 => 0x24, // BLOCK SEXTANT-36
        0x1FB23 => 0x25, // BLOCK SEXTANT-136
        0x1FB24 => 0x26, // BLOCK SEXTANT-236
        0x1FB25 => 0x27, // BLOCK SEXTANT-1236
        0x1FB26 => 0x28, // BLOCK SEXTANT-46
        0x1FB27 => 0x29, // BLOCK SEXTANT-146
        0x1FB28 => 0x2B, // BLOCK SEXTANT-1246
        0x1FB29 => 0x2C, // BLOCK SEXTANT-346
        0x1FB2A => 0x2D, // BLOCK SEXTANT-1346
        0x1FB2B => 0x2E, // BLOCK SEXTANT-2346
        0x1FB2C => 0x2F, // BLOCK SEXTANT-12346
        0x1FB2D => 0x30, // BLOCK SEXTANT-56
        0x1FB2E => 0x31, // BLOCK SEXTANT-156
        0x1FB2F => 0x32, // BLOCK SEXTANT-256
        0x1FB30 => 0x33, // BLOCK SEXTANT-1256
        0x1FB31 => 0x34, // BLOCK SEXTANT-356
        0x1FB32 => 0x35, // BLOCK SEXTANT-1356
        0x1FB33 => 0x36, // BLOCK SEXTANT-2356
        0x1FB34 => 0x37, // BLOCK SEXTANT-12356
        0x1FB35 => 0x38, // BLOCK SEXTANT-456
        0x1FB36 => 0x39, // BLOCK SEXTANT-1456
        0x1FB37 => 0x3A, // BLOCK SEXTANT-2456
        0x1FB38 => 0x3B, // BLOCK SEXTANT-12456
        0x1FB39 => 0x3C, // BLOCK SEXTANT-3456
        0x1FB3A => 0x3D, // BLOCK SEXTANT-13456
        0x1FB3B => 0x3E, // BLOCK SEXTANT-23456
        _ => 0
    };

    private static void DrawSextant(ID2D1RenderTarget target, Rect rect, ID2D1SolidColorBrush brush, byte mask)
    {
        var cellWidth = rect.Width / 2;
        var cellHeight = rect.Height / 3;
        for (var cell = 0; cell < 6; cell++)
        {
            if ((mask & (1 << cell)) != 0)
            {
                target.FillRectangle(
                    new Rect(
                        rect.X + (cell % 2) * cellWidth,
                        rect.Y + (cell / 2) * cellHeight,
                        cellWidth,
                        cellHeight),
                    brush);
            }
        }
    }

    private static bool TryDrawBlock(ID2D1RenderTarget target, int codePoint, Rect rect, ID2D1SolidColorBrush brush)
    {
        void Fill(float x, float y, float width, float height) => target.FillRectangle(
            new Rect(rect.X + x * rect.Width, rect.Y + y * rect.Height, width * rect.Width, height * rect.Height),
            brush);

        void FillEighthBands(int firstBand, int lastBand, params int[] extraBands)
        {
            for (var band = firstBand; band <= lastBand; band++)
            {
                Fill(0, (band - 1) / 8f, 1, 1 / 8f);
            }

            foreach (var band in extraBands)
            {
                Fill(0, (band - 1) / 8f, 1, 1 / 8f);
            }
        }

        switch (codePoint)
        {
            case 0x1FB70: Fill(1 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-2
            case 0x1FB71: Fill(2 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-3
            case 0x1FB72: Fill(3 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-4
            case 0x1FB73: Fill(4 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-5
            case 0x1FB74: Fill(5 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-6
            case 0x1FB75: Fill(6 / 8f, 0, 1 / 8f, 1); return true; // VERTICAL ONE EIGHTH BLOCK-7
            case 0x1FB76: Fill(0, 1 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-2
            case 0x1FB77: Fill(0, 2 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-3
            case 0x1FB78: Fill(0, 3 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-4
            case 0x1FB79: Fill(0, 4 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-5
            case 0x1FB7A: Fill(0, 5 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-6
            case 0x1FB7B: Fill(0, 6 / 8f, 1, 1 / 8f); return true; // HORIZONTAL ONE EIGHTH BLOCK-7
            case 0x1FB7C: // LEFT AND LOWER ONE EIGHTH BLOCK
                Fill(0, 0, 1 / 8f, 1);
                Fill(0, 7 / 8f, 1, 1 / 8f);
                return true;
            case 0x1FB7D: // LEFT AND UPPER ONE EIGHTH BLOCK
                Fill(0, 0, 1 / 8f, 1);
                Fill(0, 0, 1, 1 / 8f);
                return true;
            case 0x1FB7E: // RIGHT AND UPPER ONE EIGHTH BLOCK
                Fill(7 / 8f, 0, 1 / 8f, 1);
                Fill(0, 0, 1, 1 / 8f);
                return true;
            case 0x1FB7F: // RIGHT AND LOWER ONE EIGHTH BLOCK
                Fill(7 / 8f, 0, 1 / 8f, 1);
                Fill(0, 7 / 8f, 1, 1 / 8f);
                return true;
            case 0x1FB80: // UPPER AND LOWER ONE EIGHTH BLOCK
                Fill(0, 0, 1, 1 / 8f);
                Fill(0, 7 / 8f, 1, 1 / 8f);
                return true;
            case 0x1FB81: FillEighthBands(1, 1, 3, 5, 8); return true; // HORIZONTAL ONE EIGHTH BLOCK-1358
            case 0x1FB82: Fill(0, 0, 1, 2 / 8f); return true; // UPPER ONE QUARTER BLOCK
            case 0x1FB83: Fill(0, 0, 1, 3 / 8f); return true; // UPPER THREE EIGHTHS BLOCK
            case 0x1FB84: Fill(0, 0, 1, 5 / 8f); return true; // UPPER FIVE EIGHTHS BLOCK
            case 0x1FB85: Fill(0, 0, 1, 6 / 8f); return true; // UPPER THREE QUARTERS BLOCK
            case 0x1FB86: Fill(0, 0, 1, 7 / 8f); return true; // UPPER SEVEN EIGHTHS BLOCK
            case 0x1FB87: Fill(6 / 8f, 0, 2 / 8f, 1); return true; // RIGHT ONE QUARTER BLOCK
            case 0x1FB88: Fill(5 / 8f, 0, 3 / 8f, 1); return true; // RIGHT THREE EIGHTHS BLOCK
            case 0x1FB89: Fill(3 / 8f, 0, 5 / 8f, 1); return true; // RIGHT FIVE EIGHTHS BLOCK
            case 0x1FB8A: Fill(2 / 8f, 0, 6 / 8f, 1); return true; // RIGHT THREE QUARTERS BLOCK
            case 0x1FB8B: Fill(1 / 8f, 0, 7 / 8f, 1); return true; // RIGHT SEVEN EIGHTHS BLOCK
            case 0x1FB95: // CHECKER BOARD FILL; the code chart defines a four-by-four pattern.
            case 0x1FB96: // INVERSE CHECKER BOARD FILL
                for (var row = 0; row < 4; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        if (((row + column) & 1) == (codePoint == 0x1FB95 ? 0 : 1))
                        {
                            Fill(column / 4f, row / 4f, 1 / 4f, 1 / 4f);
                        }
                    }
                }
                return true;
            case 0x1FB97: FillEighthBands(3, 4, 7, 8); return true; // HEAVY HORIZONTAL FILL; BLOCK OCTANT-3478
            case 0x1FBCE: Fill(0, 0, 2 / 3f, 1); return true; // LEFT TWO THIRDS BLOCK
            case 0x1FBCF: Fill(0, 0, 1 / 3f, 1); return true; // LEFT ONE THIRD BLOCK
            case 0x1FBE4: Fill(1 / 4f, 0, 1 / 2f, 1 / 2f); return true; // UPPER CENTRE ONE QUARTER BLOCK
            case 0x1FBE5: Fill(1 / 4f, 1 / 2f, 1 / 2f, 1 / 2f); return true; // LOWER CENTRE ONE QUARTER BLOCK
            case 0x1FBE6: Fill(0, 1 / 4f, 1 / 2f, 1 / 2f); return true; // MIDDLE LEFT ONE QUARTER BLOCK
            case 0x1FBE7: Fill(1 / 2f, 1 / 4f, 1 / 2f, 1 / 2f); return true; // MIDDLE RIGHT ONE QUARTER BLOCK
            default: return false;
        }
    }

    private static DiagonalBlock? GetDiagonalBlock(int codePoint) => codePoint switch
    {
        0x1FB3C => new(DiagonalCorner.LowerLeft, GridPoint.LowerMiddleLeft, GridPoint.LowerCentre),
        0x1FB3D => new(DiagonalCorner.LowerLeft, GridPoint.LowerMiddleLeft, GridPoint.LowerRight),
        0x1FB3E => new(DiagonalCorner.LowerLeft, GridPoint.UpperMiddleLeft, GridPoint.LowerCentre),
        0x1FB3F => new(DiagonalCorner.LowerLeft, GridPoint.UpperMiddleLeft, GridPoint.LowerRight),
        0x1FB40 => new(DiagonalCorner.LowerLeft, GridPoint.UpperLeft, GridPoint.LowerCentre),
        0x1FB41 => new(DiagonalCorner.LowerRight, GridPoint.UpperMiddleLeft, GridPoint.UpperCentre),
        0x1FB42 => new(DiagonalCorner.LowerRight, GridPoint.UpperMiddleLeft, GridPoint.UpperRight),
        0x1FB43 => new(DiagonalCorner.LowerRight, GridPoint.LowerMiddleLeft, GridPoint.UpperCentre),
        0x1FB44 => new(DiagonalCorner.LowerRight, GridPoint.LowerMiddleLeft, GridPoint.UpperRight),
        0x1FB45 => new(DiagonalCorner.LowerRight, GridPoint.LowerLeft, GridPoint.UpperCentre),
        0x1FB46 => new(DiagonalCorner.LowerRight, GridPoint.LowerMiddleLeft, GridPoint.UpperMiddleRight),
        0x1FB47 => new(DiagonalCorner.LowerRight, GridPoint.LowerCentre, GridPoint.LowerMiddleRight),
        0x1FB48 => new(DiagonalCorner.LowerRight, GridPoint.LowerLeft, GridPoint.LowerMiddleRight),
        0x1FB49 => new(DiagonalCorner.LowerRight, GridPoint.LowerCentre, GridPoint.UpperMiddleRight),
        0x1FB4A => new(DiagonalCorner.LowerRight, GridPoint.LowerLeft, GridPoint.UpperMiddleRight),
        0x1FB4B => new(DiagonalCorner.LowerRight, GridPoint.LowerCentre, GridPoint.UpperRight),
        0x1FB4C => new(DiagonalCorner.LowerLeft, GridPoint.UpperCentre, GridPoint.UpperMiddleRight),
        0x1FB4D => new(DiagonalCorner.LowerLeft, GridPoint.UpperLeft, GridPoint.UpperMiddleRight),
        0x1FB4E => new(DiagonalCorner.LowerLeft, GridPoint.UpperCentre, GridPoint.LowerMiddleRight),
        0x1FB4F => new(DiagonalCorner.LowerLeft, GridPoint.UpperLeft, GridPoint.LowerMiddleRight),
        0x1FB50 => new(DiagonalCorner.LowerLeft, GridPoint.UpperCentre, GridPoint.LowerRight),
        0x1FB51 => new(DiagonalCorner.LowerLeft, GridPoint.UpperMiddleLeft, GridPoint.LowerMiddleRight),
        0x1FB52 => new(DiagonalCorner.UpperRight, GridPoint.LowerMiddleLeft, GridPoint.LowerCentre),
        0x1FB53 => new(DiagonalCorner.UpperRight, GridPoint.LowerMiddleLeft, GridPoint.LowerRight),
        0x1FB54 => new(DiagonalCorner.UpperRight, GridPoint.UpperMiddleLeft, GridPoint.LowerCentre),
        0x1FB55 => new(DiagonalCorner.UpperRight, GridPoint.UpperMiddleLeft, GridPoint.LowerRight),
        0x1FB56 => new(DiagonalCorner.UpperRight, GridPoint.UpperLeft, GridPoint.LowerCentre),
        0x1FB57 => new(DiagonalCorner.UpperLeft, GridPoint.UpperMiddleLeft, GridPoint.UpperCentre),
        0x1FB58 => new(DiagonalCorner.UpperLeft, GridPoint.UpperMiddleLeft, GridPoint.UpperRight),
        0x1FB59 => new(DiagonalCorner.UpperLeft, GridPoint.LowerMiddleLeft, GridPoint.UpperCentre),
        0x1FB5A => new(DiagonalCorner.UpperLeft, GridPoint.LowerMiddleLeft, GridPoint.UpperRight),
        0x1FB5B => new(DiagonalCorner.UpperLeft, GridPoint.LowerLeft, GridPoint.UpperCentre),
        0x1FB5C => new(DiagonalCorner.UpperLeft, GridPoint.LowerMiddleLeft, GridPoint.UpperMiddleRight),
        0x1FB5D => new(DiagonalCorner.UpperLeft, GridPoint.LowerCentre, GridPoint.LowerMiddleRight),
        0x1FB5E => new(DiagonalCorner.UpperLeft, GridPoint.LowerLeft, GridPoint.LowerMiddleRight),
        0x1FB5F => new(DiagonalCorner.UpperLeft, GridPoint.LowerCentre, GridPoint.UpperMiddleRight),
        0x1FB60 => new(DiagonalCorner.UpperLeft, GridPoint.LowerLeft, GridPoint.UpperMiddleRight),
        0x1FB61 => new(DiagonalCorner.UpperLeft, GridPoint.LowerCentre, GridPoint.UpperRight),
        0x1FB62 => new(DiagonalCorner.UpperRight, GridPoint.UpperCentre, GridPoint.UpperMiddleRight),
        0x1FB63 => new(DiagonalCorner.UpperRight, GridPoint.UpperLeft, GridPoint.UpperMiddleRight),
        0x1FB64 => new(DiagonalCorner.UpperRight, GridPoint.UpperCentre, GridPoint.LowerMiddleRight),
        0x1FB65 => new(DiagonalCorner.UpperRight, GridPoint.UpperLeft, GridPoint.LowerMiddleRight),
        0x1FB66 => new(DiagonalCorner.UpperRight, GridPoint.UpperCentre, GridPoint.LowerRight),
        0x1FB67 => new(DiagonalCorner.UpperRight, GridPoint.UpperMiddleLeft, GridPoint.LowerMiddleRight),
        _ => null
    };

    private static void DrawDiagonalBlock(
        ID2D1RenderTarget target,
        Rect rect,
        ID2D1SolidColorBrush brush,
        DiagonalBlock block)
    {
        Vector2 Point(GridPoint point) => ToCellPoint(rect, point);

        var start = Point(block.Start);
        var end = Point(block.End);
        var direction = end - start;
        var reference = Point(block.Corner switch
        {
            DiagonalCorner.UpperLeft => GridPoint.UpperLeft,
            DiagonalCorner.UpperRight => GridPoint.UpperRight,
            DiagonalCorner.LowerRight => GridPoint.LowerRight,
            _ => GridPoint.LowerLeft
        });
        var referenceSide = Cross(direction, reference - start);
        Span<Vector2> corners =
        [
            Point(GridPoint.UpperLeft),
            Point(GridPoint.UpperRight),
            Point(GridPoint.LowerRight),
            Point(GridPoint.LowerLeft)
        ];
        Span<Vector2> polygon = stackalloc Vector2[5];
        var count = 0;

        // Clip the cell to the half-plane containing the corner named by Unicode.
        for (var index = 0; index < corners.Length; index++)
        {
            var current = corners[index];
            var next = corners[(index + 1) % corners.Length];
            var currentDistance = Cross(direction, current - start) * referenceSide;
            var nextDistance = Cross(direction, next - start) * referenceSide;
            var currentInside = currentDistance >= 0;
            var nextInside = nextDistance >= 0;
            if (currentInside)
            {
                polygon[count++] = current;
            }

            if (currentInside != nextInside)
            {
                var amount = currentDistance / (currentDistance - nextDistance);
                polygon[count++] = current + (next - current) * amount;
            }
        }

        FillPolygon(target, brush, polygon[..count]);
    }

    private static bool TryDrawDiagonalLine(ID2D1RenderTarget target, int codePoint, Rect rect, ID2D1SolidColorBrush brush)
    {
        Vector2 Point(GridPoint point) => ToCellPoint(rect, point);
        void Line(GridPoint start, GridPoint end) => target.DrawLine(Point(start), Point(end), brush, 1);

        switch (codePoint)
        {
            case 0x1FBA0: Line(GridPoint.UpperCentre, GridPoint.MiddleLeft); return true;
            case 0x1FBA1: Line(GridPoint.UpperCentre, GridPoint.MiddleRight); return true;
            case 0x1FBA2: Line(GridPoint.MiddleLeft, GridPoint.LowerCentre); return true;
            case 0x1FBA3: Line(GridPoint.MiddleRight, GridPoint.LowerCentre); return true;
            case 0x1FBA4:
                Line(GridPoint.UpperCentre, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleLeft, GridPoint.LowerCentre);
                return true;
            case 0x1FBA5:
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                Line(GridPoint.MiddleRight, GridPoint.LowerCentre);
                return true;
            case 0x1FBA6:
                Line(GridPoint.MiddleLeft, GridPoint.LowerCentre);
                Line(GridPoint.LowerCentre, GridPoint.MiddleRight);
                return true;
            case 0x1FBA7:
                Line(GridPoint.MiddleLeft, GridPoint.UpperCentre);
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                return true;
            case 0x1FBA8:
                Line(GridPoint.UpperCentre, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleRight, GridPoint.LowerCentre);
                return true;
            case 0x1FBA9:
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                Line(GridPoint.MiddleLeft, GridPoint.LowerCentre);
                return true;
            case 0x1FBAA:
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                Line(GridPoint.MiddleRight, GridPoint.LowerCentre);
                Line(GridPoint.LowerCentre, GridPoint.MiddleLeft);
                return true;
            case 0x1FBAB:
                Line(GridPoint.UpperCentre, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleLeft, GridPoint.LowerCentre);
                Line(GridPoint.LowerCentre, GridPoint.MiddleRight);
                return true;
            case 0x1FBAC:
                Line(GridPoint.MiddleLeft, GridPoint.UpperCentre);
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                Line(GridPoint.MiddleRight, GridPoint.LowerCentre);
                return true;
            case 0x1FBAD:
                Line(GridPoint.MiddleRight, GridPoint.UpperCentre);
                Line(GridPoint.UpperCentre, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleLeft, GridPoint.LowerCentre);
                return true;
            case 0x1FBAE: // BOX DRAWINGS LIGHT DIAGONAL DIAMOND
                Line(GridPoint.UpperCentre, GridPoint.MiddleRight);
                Line(GridPoint.MiddleRight, GridPoint.LowerCentre);
                Line(GridPoint.LowerCentre, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleLeft, GridPoint.UpperCentre);
                return true;
            case 0x1FBAF: // BOX DRAWINGS LIGHT HORIZONTAL WITH VERTICAL STROKE
                Line(GridPoint.MiddleLeft, GridPoint.MiddleRight);
                Line(GridPoint.UpperCentre, GridPoint.LowerCentre);
                return true;
            case 0x1FBD0: Line(GridPoint.MiddleRight, GridPoint.LowerLeft); return true;
            case 0x1FBD1: Line(GridPoint.UpperRight, GridPoint.MiddleLeft); return true;
            case 0x1FBD2: Line(GridPoint.UpperLeft, GridPoint.MiddleRight); return true;
            case 0x1FBD3: Line(GridPoint.MiddleLeft, GridPoint.LowerRight); return true;
            case 0x1FBD4: Line(GridPoint.UpperLeft, GridPoint.LowerCentre); return true;
            case 0x1FBD5: Line(GridPoint.UpperCentre, GridPoint.LowerRight); return true;
            case 0x1FBD6: Line(GridPoint.UpperRight, GridPoint.LowerCentre); return true;
            case 0x1FBD7: Line(GridPoint.UpperCentre, GridPoint.LowerLeft); return true;
            case 0x1FBD8:
                Line(GridPoint.UpperLeft, GridPoint.MiddleCentre);
                Line(GridPoint.MiddleCentre, GridPoint.UpperRight);
                return true;
            case 0x1FBD9:
                Line(GridPoint.UpperRight, GridPoint.MiddleCentre);
                Line(GridPoint.MiddleCentre, GridPoint.LowerRight);
                return true;
            case 0x1FBDA:
                Line(GridPoint.LowerLeft, GridPoint.MiddleCentre);
                Line(GridPoint.MiddleCentre, GridPoint.LowerRight);
                return true;
            case 0x1FBDB:
                Line(GridPoint.UpperLeft, GridPoint.MiddleCentre);
                Line(GridPoint.MiddleCentre, GridPoint.LowerLeft);
                return true;
            case 0x1FBDC:
                Line(GridPoint.UpperLeft, GridPoint.LowerCentre);
                Line(GridPoint.LowerCentre, GridPoint.UpperRight);
                return true;
            case 0x1FBDD:
                Line(GridPoint.UpperRight, GridPoint.MiddleLeft);
                Line(GridPoint.MiddleLeft, GridPoint.LowerRight);
                return true;
            case 0x1FBDE:
                Line(GridPoint.LowerLeft, GridPoint.UpperCentre);
                Line(GridPoint.UpperCentre, GridPoint.LowerRight);
                return true;
            case 0x1FBDF:
                Line(GridPoint.UpperLeft, GridPoint.MiddleRight);
                Line(GridPoint.MiddleRight, GridPoint.LowerLeft);
                return true;
            default: return DrawTriangleBlock(target, codePoint, rect, brush);
        }
    }

    private static bool DrawTriangleBlock(ID2D1RenderTarget target, int codePoint, Rect rect, ID2D1SolidColorBrush brush)
    {
        Vector2 Point(GridPoint point) => ToCellPoint(rect, point);

        switch (codePoint)
        {
            case 0x1FB68: // UPPER AND RIGHT AND LOWER TRIANGULAR THREE QUARTERS BLOCK
                FillPolygon(target, brush,
                [
                    Point(GridPoint.MiddleCentre), Point(GridPoint.UpperLeft), Point(GridPoint.UpperRight),
                    Point(GridPoint.LowerRight), Point(GridPoint.LowerLeft)
                ]);
                return true;
            case 0x1FB69: // LEFT AND LOWER AND RIGHT TRIANGULAR THREE QUARTERS BLOCK
                FillPolygon(target, brush,
                [
                    Point(GridPoint.MiddleCentre), Point(GridPoint.UpperRight), Point(GridPoint.LowerRight),
                    Point(GridPoint.LowerLeft), Point(GridPoint.UpperLeft)
                ]);
                return true;
            case 0x1FB6A: // UPPER AND LEFT AND LOWER TRIANGULAR THREE QUARTERS BLOCK
                FillPolygon(target, brush,
                [
                    Point(GridPoint.MiddleCentre), Point(GridPoint.LowerRight), Point(GridPoint.LowerLeft),
                    Point(GridPoint.UpperLeft), Point(GridPoint.UpperRight)
                ]);
                return true;
            case 0x1FB6B: // LEFT AND UPPER AND RIGHT TRIANGULAR THREE QUARTERS BLOCK
                FillPolygon(target, brush,
                [
                    Point(GridPoint.MiddleCentre), Point(GridPoint.LowerLeft), Point(GridPoint.UpperLeft),
                    Point(GridPoint.UpperRight), Point(GridPoint.LowerRight)
                ]);
                return true;
            case 0x1FB6C: // LEFT TRIANGULAR ONE QUARTER BLOCK
                FillPolygon(target, brush, [Point(GridPoint.UpperLeft), Point(GridPoint.MiddleCentre), Point(GridPoint.LowerLeft)]);
                return true;
            case 0x1FB6D: // UPPER TRIANGULAR ONE QUARTER BLOCK
                FillPolygon(target, brush, [Point(GridPoint.UpperLeft), Point(GridPoint.UpperRight), Point(GridPoint.MiddleCentre)]);
                return true;
            case 0x1FB6E: // RIGHT TRIANGULAR ONE QUARTER BLOCK
                FillPolygon(target, brush, [Point(GridPoint.UpperRight), Point(GridPoint.LowerRight), Point(GridPoint.MiddleCentre)]);
                return true;
            case 0x1FB6F: // LOWER TRIANGULAR ONE QUARTER BLOCK
                FillPolygon(target, brush, [Point(GridPoint.MiddleCentre), Point(GridPoint.LowerRight), Point(GridPoint.LowerLeft)]);
                return true;
            case 0x1FB9A: // UPPER AND LOWER TRIANGULAR HALF BLOCK
                FillPolygon(target, brush, [Point(GridPoint.UpperLeft), Point(GridPoint.UpperRight), Point(GridPoint.MiddleCentre)]);
                FillPolygon(target, brush, [Point(GridPoint.MiddleCentre), Point(GridPoint.LowerRight), Point(GridPoint.LowerLeft)]);
                return true;
            case 0x1FB9B: // LEFT AND RIGHT TRIANGULAR HALF BLOCK
                FillPolygon(target, brush, [Point(GridPoint.UpperLeft), Point(GridPoint.MiddleCentre), Point(GridPoint.LowerLeft)]);
                FillPolygon(target, brush, [Point(GridPoint.UpperRight), Point(GridPoint.LowerRight), Point(GridPoint.MiddleCentre)]);
                return true;
            default: return false;
        }
    }

    private static Vector2 ToCellPoint(Rect rect, GridPoint point) => point switch
    {
        GridPoint.UpperLeft => new(rect.X, rect.Y),
        GridPoint.UpperCentre => new(rect.X + rect.Width / 2, rect.Y),
        GridPoint.UpperRight => new(rect.X + rect.Width, rect.Y),
        GridPoint.UpperMiddleLeft => new(rect.X, rect.Y + rect.Height / 4),
        GridPoint.UpperMiddleRight => new(rect.X + rect.Width, rect.Y + rect.Height / 4),
        GridPoint.MiddleLeft => new(rect.X, rect.Y + rect.Height / 2),
        GridPoint.MiddleCentre => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
        GridPoint.MiddleRight => new(rect.X + rect.Width, rect.Y + rect.Height / 2),
        GridPoint.LowerMiddleLeft => new(rect.X, rect.Y + rect.Height * 3 / 4),
        GridPoint.LowerMiddleRight => new(rect.X + rect.Width, rect.Y + rect.Height * 3 / 4),
        GridPoint.LowerLeft => new(rect.X, rect.Y + rect.Height),
        GridPoint.LowerCentre => new(rect.X + rect.Width / 2, rect.Y + rect.Height),
        _ => new(rect.X + rect.Width, rect.Y + rect.Height)
    };

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;

    private static void FillPolygon(ID2D1RenderTarget target, ID2D1SolidColorBrush brush, ReadOnlySpan<Vector2> points)
    {
        if (points.Length < 3)
        {
            return;
        }

        using var geometry = target.Factory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(points[0], FigureBegin.Filled);
            for (var index = 1; index < points.Length; index++)
            {
                sink.AddLine(points[index]);
            }

            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }

        target.FillGeometry(geometry, brush);
    }

    private readonly record struct DiagonalBlock(DiagonalCorner Corner, GridPoint Start, GridPoint End);

    private enum DiagonalCorner
    {
        UpperLeft,
        UpperRight,
        LowerLeft,
        LowerRight
    }

    private enum GridPoint
    {
        UpperLeft,
        UpperCentre,
        UpperRight,
        UpperMiddleLeft,
        UpperMiddleRight,
        MiddleLeft,
        MiddleCentre,
        MiddleRight,
        LowerMiddleLeft,
        LowerMiddleRight,
        LowerLeft,
        LowerCentre,
        LowerRight
    }
}
