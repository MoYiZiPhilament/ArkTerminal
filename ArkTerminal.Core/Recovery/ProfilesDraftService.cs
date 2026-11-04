using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;

namespace ArkTerminal.Core.Recovery;

public sealed class ProfilesDraftService
{
    private readonly string _path;
    private readonly JsonDocumentStore<ProfilesDraft> _store;

    public ProfilesDraftService(IAppDataPaths paths, StorageOptions options)
    {
        _path = paths.ProfilesDraftFile;
        _store = new(_path, paths.BackupDirectory, options, RecoveryValidator.ValidateProfilesDraft);
    }

    public Task<DocumentLoadResult<ProfilesDraft>> LoadAsync(CancellationToken token = default) => _store.LoadAsync(token);

    public Task<DocumentSaveResult> SaveAsync(ProfilesDraft draft, string? revision, CancellationToken token = default)
    {
        draft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return _store.SaveAsync(draft, revision, cancellationToken: token);
    }

    public async Task<DocumentSaveResult> DeleteAsync(CancellationToken token = default)
    {
        try
        {
            await Task.Run(() => File.Delete(_path), token);
            return new() { Status = DocumentSaveStatus.Success };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new() { Status = DocumentSaveStatus.IoError, Error = $"{_path}{Environment.NewLine}{exception.Message}" };
        }
    }
}
