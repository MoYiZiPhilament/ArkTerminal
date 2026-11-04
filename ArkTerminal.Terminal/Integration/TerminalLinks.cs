using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArkTerminal.Terminal.Rendering;

namespace ArkTerminal.Terminal.Integration;

public sealed record TerminalLink(string DisplayText, string Target, bool IsWeb);

public static class TerminalLinks
{
    private static readonly Regex Candidates = new("""https?://[^\s<>"']+|"[^"\r\n]+"|'[^'\r\n]+'|(?:[A-Za-z]:[\\/]|\.{1,2}[\\/])[^\s<>"']+|[^\s<>"']+[\\/][^\s<>"']+""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));

    public static TerminalLink? Find(TerminalScreenSnapshot snapshot, long lineId, int column, string? cwd)
    {
        var row = snapshot.Lines.ToList().FindIndex(line => line.LineId == lineId);
        if (row < 0) return null;
        var clicked = snapshot.Lines[row].Clusters.FirstOrDefault(cluster => column >= cluster.StartColumn && column < cluster.StartColumn + cluster.ColumnWidth);
        if (clicked?.Hyperlink is { } target) return Resolve(clicked.Text, target, cwd);
        var first = row;
        while (first > 0 && snapshot.Lines[first - 1].IsSoftWrapped) first--;
        var text = new StringBuilder();
        var index = -1;
        for (var i = first; i < snapshot.Lines.Count && text.Length < 32768; i++)
        {
            foreach (var cluster in snapshot.Lines[i].Clusters)
            {
                if (i == row && column >= cluster.StartColumn && column < cluster.StartColumn + cluster.ColumnWidth) index = text.Length;
                text.Append(cluster.Text);
            }
            if (!snapshot.Lines[i].IsSoftWrapped) break;
        }
        if (index < 0) return null;
        foreach (Match match in Candidates.Matches(text.ToString()))
            if (index >= match.Index && index < match.Index + match.Length)
            {
                var value = match.Value;
                var quoted = value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0];
                if (quoted) value = value[1..^1];
                else value = value.TrimEnd(',', ';');
                if (!quoted && value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    value = value.TrimEnd('.');
                    while (value.EndsWith(')') && value.Count(c => c == ')') > value.Count(c => c == '(')) value = value[..^1];
                    while (value.EndsWith(']') && value.Count(c => c == ']') > value.Count(c => c == '[')) value = value[..^1];
                }
                if (!value.Contains('/') && !value.Contains('\\') && !Regex.IsMatch(value, @"\.[a-zA-Z][a-zA-Z0-9]{0,15}$", RegexOptions.CultureInvariant)) return null;
                return Resolve(match.Value, value, cwd);
            }
        return null;
    }

    public static TerminalLink Resolve(string display, string target, string? cwd)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Length > 4096 || Hidden(target) || Hidden(display)) throw new ArgumentException("链接包含隐蔽控制字符或目标过长。");
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || Hidden(Uri.UnescapeDataString(target)))
                throw new ArgumentException("URL 的主机、用户信息或控制字符无效。");
            return new(display, uri.AbsoluteUri, true);
        }
        if (target.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var fileUri) || !fileUri.IsFile || fileUri.IsUnc) throw new ArgumentException("只允许本地文件链接。");
            target = fileUri.LocalPath;
        }
        else if (Regex.IsMatch(target, @"^[a-zA-Z][a-zA-Z0-9+.-]*:") && !Regex.IsMatch(target, @"^[a-zA-Z]:[\\/]"))
            throw new ArgumentException("只允许 http、https 与本地文件；不支持自定义协议。");
        if (Hidden(target) || target.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || target.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0 ||
            target.StartsWith("\\\\", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal) || target.Skip(2).Contains(':'))
            throw new ArgumentException("不允许网络、设备路径或备用数据流。");
        if (!Path.IsPathFullyQualified(target))
        {
            if (string.IsNullOrWhiteSpace(cwd) || !Path.IsPathFullyQualified(cwd) || cwd.StartsWith("\\\\", StringComparison.Ordinal))
                throw new ArgumentException("没有可靠的当前目录，无法解析相对路径。");
            if (target.Contains(':') || target.StartsWith('\\') || target.StartsWith('/')) throw new ArgumentException("相对路径格式不明确。");
            target = Path.Combine(cwd, target);
        }
        return new(display, Path.GetFullPath(target), false);
    }
    private static bool Hidden(string text) => text.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format);
}
