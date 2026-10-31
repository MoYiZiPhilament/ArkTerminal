using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Terminal.Screen;

internal readonly record struct TerminalStyle(
    TerminalColor Foreground,
    TerminalColor Background,
    bool Bold,
    bool Underline,
    bool Inverse)
{
    internal static TerminalStyle Default => new(TerminalColor.Default, TerminalColor.Default, false, false, false);
}

internal sealed class TerminalCell
{
    internal string Text { get; set; } = string.Empty;
    internal TerminalStyle Style { get; set; } = TerminalStyle.Default;
    internal bool IsContinuation { get; set; }
    internal int ColumnWidth { get; set; } = 1;

    internal TerminalCell Clone() => new()
    {
        Text = Text,
        Style = Style,
        IsContinuation = IsContinuation,
        ColumnWidth = ColumnWidth
    };

    internal void Clear(TerminalStyle style)
    {
        Text = string.Empty;
        Style = style;
        IsContinuation = false;
        ColumnWidth = 1;
    }
}

internal sealed class TerminalLine
{
    private static long _nextId;

    internal TerminalLine(int columns)
    {
        Cells = new TerminalCell[columns];
        for (var index = 0; index < columns; index++)
        {
            Cells[index] = new TerminalCell();
        }
    }

    internal long Id { get; } = Interlocked.Increment(ref _nextId);
    internal TerminalCell[] Cells { get; set; }
    internal bool IsSoftWrapped { get; set; }

    internal TerminalLine Clone()
    {
        var clone = new TerminalLine(Cells.Length) { IsSoftWrapped = IsSoftWrapped };
        for (var index = 0; index < Cells.Length; index++)
        {
            clone.Cells[index] = Cells[index].Clone();
        }
        return clone;
    }

    internal void Clear(TerminalStyle style)
    {
        foreach (var cell in Cells)
        {
            cell.Clear(style);
        }
        IsSoftWrapped = false;
    }
}

internal sealed class TerminalBuffer(int columns, int rows)
{
    internal List<TerminalLine> Lines { get; } = Enumerable.Range(0, rows).Select(_ => new TerminalLine(columns)).ToList();
    internal int CursorRow { get; set; }
    internal int CursorColumn { get; set; }
    internal bool WrapPending { get; set; }
}

internal readonly record struct TerminalModesSnapshot(
    bool ApplicationCursorKeys,
    bool BracketedPaste,
    bool AutoWrap,
    bool CursorVisible,
    bool IsAlternateScreen,
    bool Win32InputMode);
