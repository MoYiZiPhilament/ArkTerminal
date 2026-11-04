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
                issues.Add(new(path, "需要非空名称和等号：KEY=VALUE。"));
                continue;
            }
            var value = line[(separator + 1)..];
            if (value.Contains('\0')) issues.Add(new(path, "值不能包含 NUL。"));
            else if (!values.TryAdd(name, value)) issues.Add(new(path, "名称重复（不区分大小写）。"));
        }
        return ValidationResult.FromIssues(issues);
    }
}
