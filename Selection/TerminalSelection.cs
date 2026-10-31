using System.Text;
using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Selection;

public readonly record struct TerminalPosition(long LineId, int Column);

public sealed record TerminalSelection(TerminalPosition Anchor, TerminalPosition Focus)
{
    public (TerminalPosition Start, TerminalPosition End) Ordered => Anchor.LineId == Focus.LineId
        ? Anchor.Column <= Focus.Column ? (Anchor, Focus) : (Focus, Anchor)
        : Anchor.LineId < Focus.LineId ? (Anchor, Focus) : (Focus, Anchor);

    public (int Start, int End)? GetColumnRange(long lineId, int columns)
    {
        var (start, end) = Ordered;
        if (lineId < start.LineId || lineId > end.LineId)
        {
            return null;
        }
        var startColumn = lineId == start.LineId ? start.Column : 0;
        var endColumn = lineId == end.LineId ? end.Column : columns;
        return startColumn == endColumn ? null : (startColumn, endColumn);
    }
}

public static class TerminalTextExtraction
{
    public static string Copy(TerminalSelection selection, TerminalBufferTextSnapshot buffer)
    {
        var (start, end) = selection.Ordered;
        var result = new StringBuilder();
        foreach (var line in buffer.Lines)
        {
            if (line.LineId < start.LineId || line.LineId > end.LineId)
            {
                continue;
            }
            var startColumn = line.LineId == start.LineId ? start.Column : 0;
            var endColumn = line.LineId == end.LineId ? end.Column : int.MaxValue;
            var selected = line.Clusters.Where(cluster =>
                cluster.StartColumn + cluster.ColumnWidth > startColumn && cluster.StartColumn < endColumn).ToList();
            var coversFullLine = startColumn <= 0 && endColumn >= line.Clusters.Sum(cluster => cluster.ColumnWidth);
            if (coversFullLine)
            {
                while (selected.Count > 0 && !selected[^1].IsOccupied)
                {
                    selected.RemoveAt(selected.Count - 1);
                }
            }
            foreach (var cluster in selected)
            {
                result.Append(cluster.Text);
            }
            if (!line.IsSoftWrapped)
            {
                result.Append("\r\n");
            }
        }
        if (result.Length >= 2 && result[^2] == '\r' && result[^1] == '\n')
        {
            result.Length -= 2;
        }
        return result.ToString();
    }
}
