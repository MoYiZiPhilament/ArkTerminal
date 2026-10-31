using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Search;

public sealed record TerminalSearchMatch(long LineId, int LineIndex, int StartColumn, int EndColumn);

public static class TerminalSearchService
{
    public static IReadOnlyList<TerminalSearchMatch> Find(TerminalBufferTextSnapshot buffer, string query, int maximumResults, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Length == 0 || maximumResults <= 0)
        {
            return [];
        }
        var results = new List<TerminalSearchMatch>();
        for (var index = 0; index < buffer.Lines.Count && results.Count < maximumResults; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var logicalStart = index;
            var text = new System.Text.StringBuilder();
            var end = index;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.Append(buffer.Lines[end].Text);
                end++;
            } while (end < buffer.Lines.Count && buffer.Lines[end - 1].IsSoftWrapped);

            var logicalText = text.ToString();
            var offset = 0;
            while (offset + query.Length <= logicalText.Length && results.Count < maximumResults)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = logicalText.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                {
                    break;
                }
                AddMatch(buffer, logicalStart, end, match, match + query.Length, results, maximumResults, cancellationToken);
                offset = match + Math.Max(1, query.Length);
            }
            index = end - 1;
        }
        return results;
    }

    private static void AddMatch(
        TerminalBufferTextSnapshot buffer,
        int logicalStart,
        int logicalEnd,
        int matchStart,
        int matchEnd,
        List<TerminalSearchMatch> results,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var textOffset = 0;
        for (var index = logicalStart; index < logicalEnd && results.Count < maximumResults; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = buffer.Lines[index];
            var lineEnd = textOffset + line.Text.Length;
            if (matchStart < lineEnd && matchEnd > textOffset)
            {
                var start = MapColumn(line, Math.Max(textOffset, matchStart) - textOffset, false);
                var end = MapColumn(line, Math.Min(lineEnd, matchEnd) - textOffset, true);
                if (start < end)
                {
                    results.Add(new TerminalSearchMatch(line.LineId, index, start, end));
                }
            }
            textOffset = lineEnd;
        }
    }

    private static int MapColumn(TerminalLogicalLineSnapshot line, int textOffset, bool endBoundary)
    {
        var utf16 = 0;
        foreach (var cluster in line.Clusters)
        {
            if (textOffset <= utf16)
            {
                return cluster.StartColumn;
            }
            if (textOffset < utf16 + cluster.Text.Length)
            {
                return endBoundary ? cluster.StartColumn + cluster.ColumnWidth : cluster.StartColumn;
            }
            utf16 += cluster.Text.Length;
        }
        return line.Clusters.Count == 0 ? 0 : line.Clusters[^1].StartColumn + line.Clusters[^1].ColumnWidth;
    }
}
