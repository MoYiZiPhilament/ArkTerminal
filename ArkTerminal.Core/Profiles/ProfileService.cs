using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Profiles;

public interface IProfileService
{
    Task<DocumentLoadResult<ProfileCatalog>> LoadAsync(CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> SaveAsync(ProfileCatalog catalog, string? expectedRevision, CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> ResetAsync(CancellationToken cancellationToken = default);
    ValidationResult Add(ProfileCatalog catalog, TerminalProfile profile);
    ValidationResult Update(ProfileCatalog catalog, TerminalProfile profile);
    ValidationResult Remove(ProfileCatalog catalog, string profileId);
}

public sealed class ProfileService : IProfileService
{
    private readonly JsonDocumentStore<ProfileCatalog> _store;

    public ProfileService(IAppDataPaths paths, StorageOptions options)
    {
        _store = new JsonDocumentStore<ProfileCatalog>(paths.ProfilesFile, paths.BackupDirectory, options, ConfigurationValidator.ValidateProfiles);
    }

    public async Task<DocumentLoadResult<ProfileCatalog>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken);
        if (result.Status != DocumentLoadStatus.NotFound)
        {
            return result;
        }

        var catalog = ProfileDefaults.CreateCatalog();
        var saved = await SaveAsync(catalog, string.Empty, cancellationToken);
        return saved.Success
            ? new DocumentLoadResult<ProfileCatalog> { Status = DocumentLoadStatus.Success, Value = catalog, Revision = saved.Revision }
            : new DocumentLoadResult<ProfileCatalog> { Status = DocumentLoadStatus.IoError, Error = saved.Error };
    }

    public Task<DocumentSaveResult> SaveAsync(ProfileCatalog catalog, string? expectedRevision, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(catalog, expectedRevision, cancellationToken: cancellationToken);

    public Task<DocumentSaveResult> ResetAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(ProfileDefaults.CreateCatalog(), null, true, cancellationToken);

    public ValidationResult Add(ProfileCatalog catalog, TerminalProfile profile)
    {
        if (catalog.Profiles.Any(existing => existing.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationResult.FromIssues([new ValidationIssue("id", "Profile ID 已存在。")]);
        }
        catalog.Profiles.Add(profile);
        return ConfigurationValidator.ValidateProfiles(catalog);
    }

    public ValidationResult Update(ProfileCatalog catalog, TerminalProfile profile)
    {
        var index = catalog.Profiles.FindIndex(existing => existing.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return ValidationResult.FromIssues([new ValidationIssue("id", "Profile 不存在。")]);
        }
        catalog.Profiles[index] = profile;
        return ConfigurationValidator.ValidateProfiles(catalog);
    }

    public ValidationResult Remove(ProfileCatalog catalog, string profileId)
    {
        var removed = catalog.Profiles.RemoveAll(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        return removed == 0
            ? ValidationResult.FromIssues([new ValidationIssue("id", "Profile 不存在。")])
            : ConfigurationValidator.ValidateProfiles(catalog);
    }
}
