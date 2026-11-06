namespace ArkTerminal.Core.Profiles;

public sealed class ProjectRootCandidate
{
    public required string Path { get; init; }
    public required IReadOnlyList<string> Markers { get; init; }
}

public sealed class ProjectRootDetectionResult
{
    public IReadOnlyList<ProjectRootCandidate> Candidates { get; init; } = [];
    public string? Error { get; init; }
}

public static class ProjectRootDetector
{
    public static Task<ProjectRootDetectionResult> DetectAsync(string directory, int maximumParentLevels = 5, CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(directory, maximumParentLevels, cancellationToken), cancellationToken);

    private static ProjectRootDetectionResult Detect(string directory, int maximumParentLevels, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory) || directory.Contains('\0') || maximumParentLevels is < 0 or > 5)
        {
            return new ProjectRootDetectionResult { Error = Localize.T("Text0657") };
        }
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(directory));
            if (!current.Exists)
            {
                return new ProjectRootDetectionResult { Error = Localize.T("Text0658") };
            }
            var candidates = new List<ProjectRootCandidate>();
            for (var level = 0; level <= maximumParentLevels && current is not null; level++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var markers = new List<string>();
                if (File.Exists(Path.Combine(current.FullName, ".git")) || Directory.Exists(Path.Combine(current.FullName, ".git")))
                {
                    markers.Add(".git");
                }
                if (File.Exists(Path.Combine(current.FullName, "package.json")))
                {
                    markers.Add("package.json");
                }
                if (current.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
                {
                    markers.Add("*.sln");
                }
                if (markers.Count > 0)
                {
                    candidates.Add(new ProjectRootCandidate { Path = current.FullName, Markers = markers });
                }
                current = current.Parent;
            }
            return new ProjectRootDetectionResult { Candidates = candidates };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            return new ProjectRootDetectionResult { Error = Localize.F("Text0659", exception.Message) };
        }
    }
}
