using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;

namespace ArkTerminal.Core.Recovery;

public interface ISettingsDraftService
{
    Task<DocumentLoadResult<SettingsDraft>> LoadAsync(CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> SaveAsync(SettingsDraft draft, string? expectedRevision, CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> DeleteAsync(CancellationToken cancellationToken = default);
}

public sealed class SettingsDraftService : ISettingsDraftService
{
    private readonly string _path;
    private readonly JsonDocumentStore<SettingsDraft> _store;

    public SettingsDraftService(IAppDataPaths paths, StorageOptions options)
    {
        _path = paths.SettingsDraftFile;
        _store = new JsonDocumentStore<SettingsDraft>(paths.SettingsDraftFile, paths.BackupDirectory, options, RecoveryValidator.ValidateDraft);
    }

    public Task<DocumentLoadResult<SettingsDraft>> LoadAsync(CancellationToken cancellationToken = default) => _store.LoadAsync(cancellationToken);

    public Task<DocumentSaveResult> SaveAsync(SettingsDraft draft, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return _store.SaveAsync(draft, expectedRevision, cancellationToken: cancellationToken);
    }

    public async Task<DocumentSaveResult> DeleteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }, cancellationToken);
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.IoError, Error = $"{_path}{Environment.NewLine}{exception.Message}" };
        }
    }
}
