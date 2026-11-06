using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArkTerminal.Core.Context;

public static class ContextLimits
{
    public const int MaxLines = 200;
    public const int MaxBytes = 64 * 1024;
    public const int MaxPreviewBytes = 128 * 1024;
    public const int MaxCommandCharacters = 8192;
}

public sealed record ContextTextResult(string Text, bool Truncated, int Redactions);

public static class ContextText
{
    private static readonly Regex[] Secrets =
    [
        Pattern(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)"),
        Pattern(@"(?i)\bAuthorization[""']?\s*[:=]\s*(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=-]+"),
        Pattern(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+"),
        Pattern(@"(?i)\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|passwd|secret|authorization)[""']?\s*[:=]\s*(?:""(?:\\[^\r\n]|[^""\\\r\n])*""?|'(?:\\[^\r\n]|[^'\\\r\n])*'?|[^\s,;]+)"),
        Pattern(@"\b(?:sk-[A-Za-z0-9_-]{8,}|gh[pousr]_[A-Za-z0-9_]{8,}|github_pat_[A-Za-z0-9_]+|AKIA[A-Z0-9]{16})\b"),
        Pattern(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b"),
        Pattern(@"(?i)\b[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@")
    ];

    private static Regex Pattern(string pattern) => new(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static ContextTextResult Prepare(string raw, bool alreadyTruncated = false, int maxLines = ContextLimits.MaxLines, int maxBytes = ContextLimits.MaxBytes)
    {
        maxLines = Math.Clamp(maxLines, 1, ContextLimits.MaxLines);
        maxBytes = Math.Clamp(maxBytes, 1, ContextLimits.MaxBytes);
        var bounded = Limit(raw, maxLines, maxBytes);
        var text = StripControls(bounded.Text);
        var count = 0;
        try
        {
            foreach (var pattern in Secrets)
                text = pattern.Replace(text, _ => { count++; return Localize.T("Text0642"); });
        }
        catch (RegexMatchTimeoutException)
        {
            return new(Localize.T("Text0643"), true, count);
        }
        var final = Limit(text, maxLines, maxBytes);
        return new(final.Text, alreadyTruncated || bounded.Truncated || final.Truncated, count);
    }

    // Limits preserve complete .NET grapheme clusters, including combining marks and ZWJ emoji.
    public static (string Text, bool Truncated) Limit(string text, int maxLines, int maxBytes)
    {
        var position = 0;
        var bytes = 0;
        var lines = 1;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);
            var newlines = element.Count(c => c == '\n');
            if (bytes + size > maxBytes || lines + newlines > maxLines) break;
            bytes += size;
            lines += newlines;
            position = elements.ElementIndex + element.Length;
        }
        return (text[..position], position < text.Length);
    }

    public static bool IsVisibleText(string text) => !text.Any(c =>
        (char.IsControl(c) && c is not ('\r' or '\n' or '\t')) || c is '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or '\u2066' or '\u2067' or '\u2068' or '\u2069' or '\u200b' or '\ufeff');

    public static void ValidatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(Localize.T("Text0644"));
        if (Encoding.UTF8.GetByteCount(text) > ContextLimits.MaxPreviewBytes) throw new ArgumentException(Localize.T("Text0645"));
        if (!IsVisibleText(text)) throw new ArgumentException(Localize.T("Text0646"));
    }

    public static string SafeField(string? text, int maxBytes = 1024) => Prepare(text ?? Localize.T("Text0056"), maxLines: 1, maxBytes: maxBytes).Text;

    public static string StripControls(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\x1b' || c is '\u009b' or '\u009d' or '\u0090' or '\u009e' or '\u009f')
            {
                var kind = c == '\x1b' && i + 1 < text.Length ? text[++i] : c;
                if (kind is '[' or '\u009b')
                {
                    while (i + 1 < text.Length) { var next = text[++i]; if (next is >= '@' and <= '~') break; }
                }
                else if (kind is ']' or 'P' or '^' or '_' or '\u009d' or '\u0090' or '\u009e' or '\u009f')
                {
                    while (i + 1 < text.Length)
                    {
                        var next = text[++i];
                        if (next is '\a' or '\u009c') break;
                        if (next == '\x1b' && i + 1 < text.Length && text[i + 1] == '\\') { i++; break; }
                    }
                }
                continue;
            }
            if (IsVisibleText(c.ToString())) result.Append(c);
        }
        return result.ToString();
    }
}
