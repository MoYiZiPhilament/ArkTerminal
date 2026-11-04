using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace ArkTerminal.Rendering;

/// <summary>
/// Draws the Unicode Box Drawing block as cell-aligned geometry, independently of font glyph metrics.
/// </summary>
internal static class TerminalBoxDrawingRenderer
{
    internal static bool TryDraw(ID2D1RenderTarget target, char value, Rect rect, ID2D1SolidColorBrush brush)
    {
        if (value is < '\u2500' or > '\u257F')
        {
            return false;
        }

        var drawing = new BoxDrawing(target, rect, brush);
        switch (value)
        {
            // Light, heavy, and dashed lines.
            case '\u2500': drawing.Single(Arms.Left | Arms.Right); break; // ─
            case '\u2501': drawing.Single(Arms.None, Arms.Left | Arms.Right); break; // ━
            case '\u2502': drawing.Single(Arms.Up | Arms.Down); break; // │
            case '\u2503': drawing.Single(Arms.None, Arms.Up | Arms.Down); break; // ┃
            case '\u2504': drawing.DashedHorizontal(3, LineWeight.Light); break; // ┄
            case '\u2505': drawing.DashedHorizontal(3, LineWeight.Heavy); break; // ┅
            case '\u2506': drawing.DashedVertical(3, LineWeight.Light); break; // ┆
            case '\u2507': drawing.DashedVertical(3, LineWeight.Heavy); break; // ┇
            case '\u2508': drawing.DashedHorizontal(4, LineWeight.Light); break; // ┈
            case '\u2509': drawing.DashedHorizontal(4, LineWeight.Heavy); break; // ┉
            case '\u250A': drawing.DashedVertical(4, LineWeight.Light); break; // ┊
            case '\u250B': drawing.DashedVertical(4, LineWeight.Heavy); break; // ┋

            // Light/heavy corners.
            case '\u250C': drawing.Single(Arms.Right | Arms.Down); break; // ┌
            case '\u250D': drawing.Single(Arms.Down, Arms.Right); break; // ┍
            case '\u250E': drawing.Single(Arms.Right, Arms.Down); break; // ┎
            case '\u250F': drawing.Single(Arms.None, Arms.Right | Arms.Down); break; // ┏
            case '\u2510': drawing.Single(Arms.Left | Arms.Down); break; // ┐
            case '\u2511': drawing.Single(Arms.Down, Arms.Left); break; // ┑
            case '\u2512': drawing.Single(Arms.Left, Arms.Down); break; // ┒
            case '\u2513': drawing.Single(Arms.None, Arms.Left | Arms.Down); break; // ┓
            case '\u2514': drawing.Single(Arms.Up | Arms.Right); break; // └
            case '\u2515': drawing.Single(Arms.Up, Arms.Right); break; // ┕
            case '\u2516': drawing.Single(Arms.Right, Arms.Up); break; // ┖
            case '\u2517': drawing.Single(Arms.None, Arms.Up | Arms.Right); break; // ┗
            case '\u2518': drawing.Single(Arms.Left | Arms.Up); break; // ┘
            case '\u2519': drawing.Single(Arms.Up, Arms.Left); break; // ┙
            case '\u251A': drawing.Single(Arms.Left, Arms.Up); break; // ┚
            case '\u251B': drawing.Single(Arms.None, Arms.Left | Arms.Up); break; // ┛

            // Vertical lines with a right branch.
            case '\u251C': drawing.Single(Arms.Up | Arms.Down | Arms.Right); break; // ├
            case '\u251D': drawing.Single(Arms.Up | Arms.Down, Arms.Right); break; // ┝
            case '\u251E': drawing.Single(Arms.Right | Arms.Down, Arms.Up); break; // ┞
            case '\u251F': drawing.Single(Arms.Up | Arms.Right, Arms.Down); break; // ┟
            case '\u2520': drawing.Single(Arms.Right, Arms.Up | Arms.Down); break; // ┠
            case '\u2521': drawing.Single(Arms.Down, Arms.Up | Arms.Right); break; // ┡
            case '\u2522': drawing.Single(Arms.Up, Arms.Right | Arms.Down); break; // ┢
            case '\u2523': drawing.Single(Arms.None, Arms.Up | Arms.Down | Arms.Right); break; // ┣

            // Vertical lines with a left branch.
            case '\u2524': drawing.Single(Arms.Up | Arms.Down | Arms.Left); break; // ┤
            case '\u2525': drawing.Single(Arms.Up | Arms.Down, Arms.Left); break; // ┥
            case '\u2526': drawing.Single(Arms.Left | Arms.Down, Arms.Up); break; // ┦
            case '\u2527': drawing.Single(Arms.Up | Arms.Left, Arms.Down); break; // ┧
            case '\u2528': drawing.Single(Arms.Left, Arms.Up | Arms.Down); break; // ┨
            case '\u2529': drawing.Single(Arms.Down, Arms.Up | Arms.Left); break; // ┩
            case '\u252A': drawing.Single(Arms.Up, Arms.Left | Arms.Down); break; // ┪
            case '\u252B': drawing.Single(Arms.None, Arms.Up | Arms.Down | Arms.Left); break; // ┫

            // Horizontal lines with a down branch.
            case '\u252C': drawing.Single(Arms.Left | Arms.Right | Arms.Down); break; // ┬
            case '\u252D': drawing.Single(Arms.Right | Arms.Down, Arms.Left); break; // ┭
            case '\u252E': drawing.Single(Arms.Left | Arms.Down, Arms.Right); break; // ┮
            case '\u252F': drawing.Single(Arms.Down, Arms.Left | Arms.Right); break; // ┯
            case '\u2530': drawing.Single(Arms.Left | Arms.Right, Arms.Down); break; // ┰
            case '\u2531': drawing.Single(Arms.Right, Arms.Left | Arms.Down); break; // ┱
            case '\u2532': drawing.Single(Arms.Left, Arms.Right | Arms.Down); break; // ┲
            case '\u2533': drawing.Single(Arms.None, Arms.Left | Arms.Right | Arms.Down); break; // ┳

            // Horizontal lines with an up branch.
            case '\u2534': drawing.Single(Arms.Left | Arms.Right | Arms.Up); break; // ┴
            case '\u2535': drawing.Single(Arms.Up | Arms.Right, Arms.Left); break; // ┵
            case '\u2536': drawing.Single(Arms.Left | Arms.Up, Arms.Right); break; // ┶
            case '\u2537': drawing.Single(Arms.Up, Arms.Left | Arms.Right); break; // ┷
            case '\u2538': drawing.Single(Arms.Left | Arms.Right, Arms.Up); break; // ┸
            case '\u2539': drawing.Single(Arms.Right, Arms.Left | Arms.Up); break; // ┹
            case '\u253A': drawing.Single(Arms.Left, Arms.Up | Arms.Right); break; // ┺
            case '\u253B': drawing.Single(Arms.None, Arms.Left | Arms.Right | Arms.Up); break; // ┻

            // Four-way light/heavy junctions. The two arms named together are heavy where specified.
            case '\u253C': drawing.Single(Arms.Left | Arms.Right | Arms.Up | Arms.Down); break; // ┼
            case '\u253D': drawing.Single(Arms.Up | Arms.Down | Arms.Right, Arms.Left); break; // ┽
            case '\u253E': drawing.Single(Arms.Left | Arms.Up | Arms.Down, Arms.Right); break; // ┾
            case '\u253F': drawing.Single(Arms.Up | Arms.Down, Arms.Left | Arms.Right); break; // ┿
            case '\u2540': drawing.Single(Arms.Left | Arms.Right | Arms.Down, Arms.Up); break; // ╀
            case '\u2541': drawing.Single(Arms.Left | Arms.Right | Arms.Up, Arms.Down); break; // ╁
            case '\u2542': drawing.Single(Arms.Left | Arms.Right, Arms.Up | Arms.Down); break; // ╂
            case '\u2543': drawing.Single(Arms.Right | Arms.Down, Arms.Left | Arms.Up); break; // ╃
            case '\u2544': drawing.Single(Arms.Left | Arms.Down, Arms.Up | Arms.Right); break; // ╄
            case '\u2545': drawing.Single(Arms.Up | Arms.Right, Arms.Left | Arms.Down); break; // ╅
            case '\u2546': drawing.Single(Arms.Left | Arms.Up, Arms.Right | Arms.Down); break; // ╆
            case '\u2547': drawing.Single(Arms.Down, Arms.Left | Arms.Up | Arms.Right); break; // ╇
            case '\u2548': drawing.Single(Arms.Up, Arms.Left | Arms.Right | Arms.Down); break; // ╈
            case '\u2549': drawing.Single(Arms.Right, Arms.Left | Arms.Up | Arms.Down); break; // ╉
            case '\u254A': drawing.Single(Arms.Left, Arms.Up | Arms.Right | Arms.Down); break; // ╊
            case '\u254B': drawing.Single(Arms.None, Arms.Left | Arms.Right | Arms.Up | Arms.Down); break; // ╋

            // Double-dash lines (two dashes, not double strokes).
            case '\u254C': drawing.DashedHorizontal(2, LineWeight.Light); break; // ╌
            case '\u254D': drawing.DashedHorizontal(2, LineWeight.Heavy); break; // ╍
            case '\u254E': drawing.DashedVertical(2, LineWeight.Light); break; // ╎
            case '\u254F': drawing.DashedVertical(2, LineWeight.Heavy); break; // ╏

            // Double lines and mixed single/double corners.
            case '\u2550': drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY); break; // ═
            case '\u2551': drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX); break; // ║
            case '\u2552': // ╒ down single, right double
                drawing.DoubleHorizontal(drawing.CX, drawing.X1, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.CY - drawing.Offset, drawing.Y1, LineWeight.Light);
                break;
            case '\u2553': // ╓ down double, right single
                drawing.DoubleVertical(drawing.CY, drawing.Y1, drawing.CX);
                drawing.Horizontal(drawing.CX - drawing.Offset, drawing.X1, drawing.CY, LineWeight.Light);
                break;
            case '\u2554': drawing.DoubleDownRight(); break; // ╔
            case '\u2555': // ╕ down single, left double
                drawing.DoubleHorizontal(drawing.X0, drawing.CX, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.CY - drawing.Offset, drawing.Y1, LineWeight.Light);
                break;
            case '\u2556': // ╖ down double, left single
                drawing.DoubleVertical(drawing.CY, drawing.Y1, drawing.CX);
                drawing.Horizontal(drawing.X0, drawing.CX + drawing.Offset, drawing.CY, LineWeight.Light);
                break;
            case '\u2557': drawing.DoubleDownLeft(); break; // ╗
            case '\u2558': // ╘ up single, right double
                drawing.DoubleHorizontal(drawing.CX, drawing.X1, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.Y0, drawing.CY + drawing.Offset, LineWeight.Light);
                break;
            case '\u2559': // ╙ up double, right single
                drawing.DoubleVertical(drawing.Y0, drawing.CY, drawing.CX);
                drawing.Horizontal(drawing.CX - drawing.Offset, drawing.X1, drawing.CY, LineWeight.Light);
                break;
            case '\u255A': drawing.DoubleUpRight(); break; // ╚
            case '\u255B': // ╛ up single, left double
                drawing.DoubleHorizontal(drawing.X0, drawing.CX, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.Y0, drawing.CY + drawing.Offset, LineWeight.Light);
                break;
            case '\u255C': // ╜ up double, left single
                drawing.DoubleVertical(drawing.Y0, drawing.CY, drawing.CX);
                drawing.Horizontal(drawing.X0, drawing.CX + drawing.Offset, drawing.CY, LineWeight.Light);
                break;
            case '\u255D': drawing.DoubleUpLeft(); break; // ╝

            // Vertical branches with double lines.
            case '\u255E': // ╞ vertical single, right double
                drawing.Single(Arms.Up | Arms.Down);
                drawing.DoubleHorizontal(drawing.CX, drawing.X1, drawing.CY);
                break;
            case '\u255F': // ╟ vertical double, right single
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.Horizontal(drawing.CX - drawing.Offset, drawing.X1, drawing.CY, LineWeight.Light);
                break;
            case '\u2560': // ╠ double vertical and right
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.DoubleHorizontal(drawing.CX + drawing.Offset, drawing.X1, drawing.CY);
                break;
            case '\u2561': // ╡ vertical single, left double
                drawing.Single(Arms.Up | Arms.Down);
                drawing.DoubleHorizontal(drawing.X0, drawing.CX, drawing.CY);
                break;
            case '\u2562': // ╢ vertical double, left single
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.Horizontal(drawing.X0, drawing.CX + drawing.Offset, drawing.CY, LineWeight.Light);
                break;
            case '\u2563': // ╣ double vertical and left
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.DoubleHorizontal(drawing.X0, drawing.CX - drawing.Offset, drawing.CY);
                break;

            // Horizontal branches with double lines.
            case '\u2564': // ╤ down single, horizontal double
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.CY + drawing.Offset, drawing.Y1, LineWeight.Light);
                break;
            case '\u2565': // ╥ down double, horizontal single
                drawing.Single(Arms.Left | Arms.Right);
                drawing.DoubleVertical(drawing.CY, drawing.Y1, drawing.CX);
                break;
            case '\u2566': // ╦ double down and horizontal
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                drawing.DoubleVertical(drawing.CY + drawing.Offset, drawing.Y1, drawing.CX);
                break;
            case '\u2567': // ╧ up single, horizontal double
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                drawing.Vertical(drawing.CX, drawing.Y0, drawing.CY - drawing.Offset, LineWeight.Light);
                break;
            case '\u2568': // ╨ up double, horizontal single
                drawing.Single(Arms.Left | Arms.Right);
                drawing.DoubleVertical(drawing.Y0, drawing.CY, drawing.CX);
                break;
            case '\u2569': // ╩ double up and horizontal
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                drawing.DoubleVertical(drawing.Y0, drawing.CY - drawing.Offset, drawing.CX);
                break;
            case '\u256A': // ╪ vertical single, horizontal double
                drawing.Single(Arms.Up | Arms.Down);
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                break;
            case '\u256B': // ╫ vertical double, horizontal single
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.Single(Arms.Left | Arms.Right);
                break;
            case '\u256C': // ╬ double vertical and horizontal
                drawing.DoubleVertical(drawing.Y0, drawing.Y1, drawing.CX);
                drawing.DoubleHorizontal(drawing.X0, drawing.X1, drawing.CY);
                break;

            // Rounded corners and diagonals.
            case '\u256D': drawing.ArcDownRight(); break; // ╭
            case '\u256E': drawing.ArcDownLeft(); break; // ╮
            case '\u256F': drawing.ArcUpLeft(); break; // ╯
            case '\u2570': drawing.ArcUpRight(); break; // ╰
            case '\u2571': drawing.DiagonalUpRight(); break; // ╱
            case '\u2572': drawing.DiagonalDownRight(); break; // ╲
            case '\u2573': drawing.DiagonalUpRight(); drawing.DiagonalDownRight(); break; // ╳

            // Half lines and bidirectional light/heavy mixtures.
            case '\u2574': drawing.Single(Arms.Left); break; // ╴
            case '\u2575': drawing.Single(Arms.Up); break; // ╵
            case '\u2576': drawing.Single(Arms.Right); break; // ╶
            case '\u2577': drawing.Single(Arms.Down); break; // ╷
            case '\u2578': drawing.Single(Arms.None, Arms.Left); break; // ╸
            case '\u2579': drawing.Single(Arms.None, Arms.Up); break; // ╹
            case '\u257A': drawing.Single(Arms.None, Arms.Right); break; // ╺
            case '\u257B': drawing.Single(Arms.None, Arms.Down); break; // ╻
            case '\u257C': drawing.Single(Arms.Left, Arms.Right); break; // ╼
            case '\u257D': drawing.Single(Arms.Up, Arms.Down); break; // ╽
            case '\u257E': drawing.Single(Arms.Right, Arms.Left); break; // ╾
            case '\u257F': drawing.Single(Arms.Down, Arms.Up); break; // ╿
            default:
                throw new InvalidOperationException($"Missing box-drawing mapping for U+{(int)value:X4}.");
        }

        return true;
    }

    [Flags]
    private enum Arms : byte
    {
        None = 0,
        Left = 1,
        Right = 2,
        Up = 4,
        Down = 8
    }

    private enum LineWeight : byte
    {
        Light,
        Heavy
    }

    private readonly struct BoxDrawing
    {
        private readonly ID2D1RenderTarget _target;
        private readonly ID2D1SolidColorBrush _brush;
        private readonly float _light;
        private readonly float _heavy;

        internal BoxDrawing(ID2D1RenderTarget target, Rect rect, ID2D1SolidColorBrush brush)
        {
            _target = target;
            _brush = brush;
            X0 = rect.X;
            Y0 = rect.Y;
            X1 = rect.X + rect.Width;
            Y1 = rect.Y + rect.Height;
            CX = rect.X + rect.Width / 2f;
            CY = rect.Y + rect.Height / 2f;
            _light = Math.Max(1f, rect.Height / 18f);
            _heavy = _light * 1.8f;
            Offset = Math.Max(_light * 1.5f, rect.Height / 12f);
        }

        internal float X0 { get; }
        internal float Y0 { get; }
        internal float X1 { get; }
        internal float Y1 { get; }
        internal float CX { get; }
        internal float CY { get; }
        internal float Offset { get; }

        internal void Single(Arms lightArms, Arms heavyArms = Arms.None)
        {
            DrawArms(lightArms, LineWeight.Light);
            DrawArms(heavyArms, LineWeight.Heavy);

            var arms = lightArms | heavyArms;
            if (CountArms(arms) > 1)
            {
                // Fill the join itself so flat line caps cannot leave a pinhole or an outer-corner notch.
                var thickness = heavyArms == Arms.None ? _light : _heavy;
                _target.FillRectangle(new Rect(CX - thickness / 2f, CY - thickness / 2f, thickness, thickness), _brush);
            }
        }

        internal void Horizontal(float x1, float x2, float y, LineWeight weight) =>
            Segment(new Vector2(x1, y), new Vector2(x2, y), weight);

        internal void Vertical(float x, float y1, float y2, LineWeight weight) =>
            Segment(new Vector2(x, y1), new Vector2(x, y2), weight);

        internal void DoubleHorizontal(float x1, float x2, float y)
        {
            Horizontal(x1, x2, y - Offset, LineWeight.Light);
            Horizontal(x1, x2, y + Offset, LineWeight.Light);
        }

        internal void DoubleVertical(float y1, float y2, float x)
        {
            Vertical(x - Offset, y1, y2, LineWeight.Light);
            Vertical(x + Offset, y1, y2, LineWeight.Light);
        }

        internal void DashedHorizontal(int count, LineWeight weight)
        {
            var period = (X1 - X0) / count;
            var dash = period * 0.6f;
            for (var index = 0; index < count; index++)
            {
                var center = X0 + (index + 0.5f) * period;
                Horizontal(center - dash / 2f, center + dash / 2f, CY, weight);
            }
        }

        internal void DashedVertical(int count, LineWeight weight)
        {
            var period = (Y1 - Y0) / count;
            var dash = period * 0.6f;
            for (var index = 0; index < count; index++)
            {
                var center = Y0 + (index + 0.5f) * period;
                Vertical(CX, center - dash / 2f, center + dash / 2f, weight);
            }
        }

        internal void DoubleDownRight()
        {
            BendRightDown(CX - Offset, CY - Offset);
            BendRightDown(CX + Offset, CY + Offset);
        }

        internal void DoubleDownLeft()
        {
            BendLeftDown(CX + Offset, CY - Offset);
            BendLeftDown(CX - Offset, CY + Offset);
        }

        internal void DoubleUpRight()
        {
            BendRightUp(CX - Offset, CY + Offset);
            BendRightUp(CX + Offset, CY - Offset);
        }

        internal void DoubleUpLeft()
        {
            BendLeftUp(CX + Offset, CY + Offset);
            BendLeftUp(CX - Offset, CY - Offset);
        }

        internal void ArcDownRight()
        {
            var radius = MathF.Min(CX - X0, Y1 - CY);
            Horizontal(CX + radius, X1, CY, LineWeight.Light);
            QuarterArc(CX + radius, CY + radius, radius, -MathF.PI / 2f, -MathF.PI);
            Vertical(CX, CY + radius, Y1, LineWeight.Light);
        }

        internal void ArcDownLeft()
        {
            var radius = MathF.Min(CX - X0, Y1 - CY);
            Horizontal(X0, CX - radius, CY, LineWeight.Light);
            QuarterArc(CX - radius, CY + radius, radius, -MathF.PI / 2f, 0f);
            Vertical(CX, CY + radius, Y1, LineWeight.Light);
        }

        internal void ArcUpLeft()
        {
            var radius = MathF.Min(CX - X0, CY - Y0);
            Horizontal(X0, CX - radius, CY, LineWeight.Light);
            QuarterArc(CX - radius, CY - radius, radius, MathF.PI / 2f, 0f);
            Vertical(CX, Y0, CY - radius, LineWeight.Light);
        }

        internal void ArcUpRight()
        {
            var radius = MathF.Min(CX - X0, CY - Y0);
            Horizontal(CX + radius, X1, CY, LineWeight.Light);
            QuarterArc(CX + radius, CY - radius, radius, MathF.PI / 2f, MathF.PI);
            Vertical(CX, Y0, CY - radius, LineWeight.Light);
        }

        internal void DiagonalUpRight() =>
            Segment(new Vector2(X0, Y1), new Vector2(X1, Y0), LineWeight.Light);

        internal void DiagonalDownRight() =>
            Segment(new Vector2(X0, Y0), new Vector2(X1, Y1), LineWeight.Light);

        private void DrawArms(Arms arms, LineWeight weight)
        {
            if ((arms & Arms.Left) != 0)
            {
                Horizontal(X0, CX, CY, weight);
            }
            if ((arms & Arms.Right) != 0)
            {
                Horizontal(CX, X1, CY, weight);
            }
            if ((arms & Arms.Up) != 0)
            {
                Vertical(CX, Y0, CY, weight);
            }
            if ((arms & Arms.Down) != 0)
            {
                Vertical(CX, CY, Y1, weight);
            }
        }

        private void BendRightDown(float x, float y)
        {
            Horizontal(x, X1, y, LineWeight.Light);
            Vertical(x, y, Y1, LineWeight.Light);
            Joint(x, y);
        }

        private void BendLeftDown(float x, float y)
        {
            Horizontal(X0, x, y, LineWeight.Light);
            Vertical(x, y, Y1, LineWeight.Light);
            Joint(x, y);
        }

        private void BendRightUp(float x, float y)
        {
            Horizontal(x, X1, y, LineWeight.Light);
            Vertical(x, Y0, y, LineWeight.Light);
            Joint(x, y);
        }

        private void BendLeftUp(float x, float y)
        {
            Horizontal(X0, x, y, LineWeight.Light);
            Vertical(x, Y0, y, LineWeight.Light);
            Joint(x, y);
        }

        private void Joint(float x, float y) =>
            _target.FillRectangle(new Rect(x - _light / 2f, y - _light / 2f, _light, _light), _brush);

        private void QuarterArc(float centerX, float centerY, float radius, float startAngle, float endAngle)
        {
            const int segmentCount = 16;
            var previous = new Vector2(centerX + radius * MathF.Cos(startAngle), centerY + radius * MathF.Sin(startAngle));
            for (var index = 1; index <= segmentCount; index++)
            {
                var angle = startAngle + (endAngle - startAngle) * index / segmentCount;
                var next = new Vector2(centerX + radius * MathF.Cos(angle), centerY + radius * MathF.Sin(angle));
                Segment(previous, next, LineWeight.Light);
                previous = next;
            }
        }

        private void Segment(Vector2 start, Vector2 end, LineWeight weight) =>
            _target.DrawLine(start, end, _brush, weight == LineWeight.Heavy ? _heavy : _light);

        private static int CountArms(Arms arms)
        {
            var count = 0;
            if ((arms & Arms.Left) != 0) count++;
            if ((arms & Arms.Right) != 0) count++;
            if ((arms & Arms.Up) != 0) count++;
            if ((arms & Arms.Down) != 0) count++;
            return count;
        }
    }
}
