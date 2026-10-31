using ArkTerminal.Core.Data;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Storage;

namespace ArkTerminal.Core.Configuration;

public interface ISettingsService
{
    Task<DocumentLoadResult<AppSettings>> LoadAsync(IReadOnlyCollection<string>? profileIds, CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> SaveAsync(AppSettings settings, IReadOnlyCollection<string>? profileIds, string? expectedRevision, CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> ResetAsync(IReadOnlyCollection<string>? profileIds, CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService
{
    private readonly JsonDocumentStore<AppSettings> _store;

    public SettingsService(IAppDataPaths paths, StorageOptions options)
    {
        _store = new JsonDocumentStore<AppSettings>(
            paths.SettingsFile,
            paths.BackupDirectory,
            options,
            settings => ConfigurationValidator.ValidateSettings(settings));
    }

    public async Task<DocumentLoadResult<AppSettings>> LoadAsync(IReadOnlyCollection<string>? profileIds, CancellationToken cancellationToken = default)
    {
        var result = await _store.LoadAsync(cancellationToken);
        if (result.Status != DocumentLoadStatus.NotFound)
        {
            if (result.Success)
            {
                var validation = ConfigurationValidator.ValidateSettings(result.Value, profileIds);
                if (!validation.IsValid)
                {
                    return new DocumentLoadResult<AppSettings> { Status = DocumentLoadStatus.Invalid, Error = validation.Summary };
                }
            }
            return result;
        }

        var settings = ConfigurationDefaults.CreateSettings();
        if (settings.DefaultProfileId is not null && profileIds?.Contains(settings.DefaultProfileId) == false)
        {
            settings.DefaultProfileId = profileIds.FirstOrDefault();
        }
        var saved = await SaveAsync(settings, profileIds, string.Empty, cancellationToken);
        return saved.Success
            ? new DocumentLoadResult<AppSettings> { Status = DocumentLoadStatus.Success, Value = settings, Revision = saved.Revision }
            : new DocumentLoadResult<AppSettings> { Status = DocumentLoadStatus.IoError, Error = saved.Error };
    }

    public Task<DocumentSaveResult> SaveAsync(AppSettings settings, IReadOnlyCollection<string>? profileIds, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        var validation = ConfigurationValidator.ValidateSettings(settings, profileIds);
        if (!validation.IsValid)
        {
            return Task.FromResult(new DocumentSaveResult { Status = DocumentSaveStatus.Invalid, Validation = validation, Error = validation.Summary });
        }
        return _store.SaveAsync(settings, expectedRevision, cancellationToken: cancellationToken);
    }

    public Task<DocumentSaveResult> ResetAsync(IReadOnlyCollection<string>? profileIds, CancellationToken cancellationToken = default)
    {
        var settings = ConfigurationDefaults.CreateSettings();
        if (settings.DefaultProfileId is not null && profileIds?.Contains(settings.DefaultProfileId) == false)
        {
            settings.DefaultProfileId = profileIds.FirstOrDefault();
        }
        return _store.SaveAsync(settings, null, true, cancellationToken);
    }
}
