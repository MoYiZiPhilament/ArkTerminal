using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Recovery;

public sealed class SettingsDraft
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public string DraftId { get; set; } = string.Empty;
    public string BaseRevision { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool CommitRequested { get; set; }
    public AppSettings Settings { get; set; } = new();
    public Dictionary<string, string> EditorValues { get; set; } = [];
}

public sealed class ProfilesDraft
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public string DraftId { get; set; } = string.Empty;
    public string BaseRevision { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool CommitRequested { get; set; }
    public ProfileCatalog Catalog { get; set; } = new();
    public string? SelectedProfileId { get; set; }
    public Dictionary<string, Dictionary<string, string>> EditorValues { get; set; } = [];
}

public sealed class RecoveryTabState
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = "{{home}}";
    public bool? WorkingDirectoryIsExplicit { get; set; }
    public string? WorkspaceId { get; set; }
    public int Order { get; set; }
}

public sealed class RecoveryState
{
    public int SchemaVersion { get; set; } = ConfigurationDefaults.CurrentSchemaVersion;
    public string? SettingsDraftId { get; set; }
    public string? ProfilesDraftId { get; set; }
    public string? ActiveTabId { get; set; }
    public List<RecoveryTabState> Tabs { get; set; } = [];
}

public static class RecoveryValidator
{
    public static ValidationResult ValidateProfilesDraft(ProfilesDraft? draft)
    {
        if (draft is null || draft.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion || string.IsNullOrWhiteSpace(draft.DraftId) || draft.EditorValues is null)
            return ValidationResult.FromIssues([new ValidationIssue("draft", "Profile 草稿无效。")]);
        return ConfigurationValidator.ValidateProfiles(draft.Catalog);
    }

    public static ValidationResult ValidateDraft(SettingsDraft? draft)
    {
        if (draft is null)
        {
            return ValidationResult.FromIssues([new ValidationIssue("draft", "草稿不能为空。")]);
        }

        var issues = new List<ValidationIssue>();
        if (draft.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", "草稿版本不受支持。"));
        }
        if (string.IsNullOrWhiteSpace(draft.DraftId))
        {
            issues.Add(new("draftId", "草稿 ID 不能为空。"));
        }
        var settings = ConfigurationValidator.ValidateSettings(draft.Settings);
        issues.AddRange(settings.Issues);
        return ValidationResult.FromIssues(issues);
    }

    public static ValidationResult ValidateState(RecoveryState? state)
    {
        if (state is null)
        {
            return ValidationResult.FromIssues([new ValidationIssue("recovery", "恢复状态不能为空。")]);
        }

        var issues = new List<ValidationIssue>();
        if (state.SchemaVersion != ConfigurationDefaults.CurrentSchemaVersion)
        {
            issues.Add(new("schemaVersion", "恢复状态版本不受支持。"));
        }
        if (state.Tabs is null || state.Tabs.Count > 100)
        {
            issues.Add(new("tabs", "恢复标签不能为空且最多 100 项。"));
            return ValidationResult.FromIssues(issues);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        for (var index = 0; index < state.Tabs.Count; index++)
        {
            var tab = state.Tabs[index];
            if (tab is null || string.IsNullOrWhiteSpace(tab.Id) || !ids.Add(tab.Id) ||
                string.IsNullOrWhiteSpace(tab.ProfileId) || string.IsNullOrWhiteSpace(tab.WorkingDirectory) ||
                tab.WorkingDirectory.Contains('\0') || !orders.Add(tab.Order))
            {
                issues.Add(new($"tabs[{index}]", "标签 ID/Profile 引用不能为空，ID 与顺序不能重复。"));
            }
        }
        return ValidationResult.FromIssues(issues);
    }
}
