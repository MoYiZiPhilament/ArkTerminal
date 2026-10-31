namespace ArkTerminal.Core.Validation;

public sealed record ValidationIssue(string Path, string Message);

public sealed class ValidationResult
{
    private static readonly ValidationResult ValidResult = new([]);
    private ValidationResult(IReadOnlyList<ValidationIssue> issues) => Issues = issues;
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsValid => Issues.Count == 0;
    public string Summary => string.Join(Environment.NewLine, Issues.Select(issue => $"{issue.Path}: {issue.Message}"));

    public static ValidationResult Valid() => ValidResult;
    public static ValidationResult FromIssues(IEnumerable<ValidationIssue> issues)
    {
        var materialized = issues.ToArray();
        return materialized.Length == 0 ? ValidResult : new ValidationResult(materialized);
    }
}
