using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Profiles;

public sealed class RecentProject
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class RecentProjectsDocument
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public List<RecentProject> Projects { get; set; } = [];
}

public interface IRecentProjectsService
{
    Task<DocumentLoadResult<RecentProjectsDocument>> LoadAsync(CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> SaveAsync(RecentProjectsDocument document, string? expectedRevision, CancellationToken cancellationToken = default);
}

public sealed class RecentProjectsService : IRecentProjectsService
{
    public const int MaximumProjects = 20;
    private readonly JsonDocumentStore<RecentProjectsDocument> _store;

    public RecentProjectsService(IAppDataPaths paths, StorageOptions options)
    {
        _store = new JsonDocumentStore<RecentProjectsDocument>(paths.RecentProjectsFile, paths.BackupDirectory, options, Validate);
    }

    public async Task<DocumentLoadResult<RecentProjectsDocument>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken);
        return result.Status == DocumentLoadStatus.NotFound
            ? new DocumentLoadResult<RecentProjectsDocument> { Status = DocumentLoadStatus.Success, Value = new RecentProjectsDocument(), Revision = string.Empty }
            : result;
    }

    public Task<DocumentSaveResult> SaveAsync(RecentProjectsDocument document, string? expectedRevision, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(document, expectedRevision, cancellationToken: cancellationToken);

    private static ValidationResult Validate(RecentProjectsDocument? document)
    {
        var issues = new List<ValidationIssue>();
        if (document is null || document.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", "最近项目文档或版本无效。"));
        }
        if (document?.Projects is null || document.Projects.Count > MaximumProjects)
        {
            issues.Add(new("projects", $"最近项目最多 {MaximumProjects} 项。"));
            return ValidationResult.FromIssues(issues);
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < document.Projects.Count; index++)
        {
            var project = document.Projects[index];
            if (project is null || string.IsNullOrWhiteSpace(project.Path) || project.Path.Contains('\0') ||
                string.IsNullOrWhiteSpace(project.DisplayName) || project.DisplayName.Length > 256 || !paths.Add(project.Path))
            {
                issues.Add(new($"projects[{index}]", "项目路径、显示名或唯一性无效。"));
            }
        }
        return ValidationResult.FromIssues(issues);
    }
}
