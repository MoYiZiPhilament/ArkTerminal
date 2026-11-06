using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Profiles;

public static class EnvironmentRows
{
    public static ValidationResult Parse(string text, out Dictionary<string, string> values)
    {
        values = new(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ValidationIssue>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var separator = line.IndexOf('=');
            var name = separator < 0 ? string.Empty : line[..separator].Trim();
            var path = $"environment.line[{index + 1}]";
            if (separator < 0 || string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\0', '\r', '\n']) >= 0)
            {
                issues.Add(new(path, Localize.T("Text0652")));
                continue;
            }
            var value = line[(separator + 1)..];
            if (value.Contains('\0')) issues.Add(new(path, Localize.T("Text0653")));
            else if (!values.TryAdd(name, value)) issues.Add(new(path, Localize.T("Text0654")));
        }
        return ValidationResult.FromIssues(issues);
    }
}
