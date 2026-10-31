using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;

namespace ArkTerminal.Core.Recovery;

public sealed class RecoveryStartupResult
{
    public bool AbnormalExit { get; init; }
    public DocumentLoadResult<RecoveryState>? State { get; init; }
    public DocumentLoadResult<SettingsDraft>? Draft { get; init; }
    public DocumentSaveResult? MarkerError { get; init; }
}

public interface IRecoveryStateService
{
    Task<RecoveryStartupResult> LoadForStartupAsync(CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> SaveAsync(RecoveryState state, string? expectedRevision, CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> DiscardAsync(CancellationToken cancellationToken = default);
    Task<DocumentSaveResult> CompleteNormalShutdownAsync(CancellationToken cancellationToken = default);
}

public sealed class RecoveryStateService : IRecoveryStateService
{
    private readonly IAppDataPaths _paths;
    private readonly ISettingsDraftService _drafts;
    private readonly JsonDocumentStore<RecoveryState> _store;

    public RecoveryStateService(IAppDataPaths paths, ISettingsDraftService drafts, StorageOptions options)
    {
        _paths = paths;
        _drafts = drafts;
        _store = new JsonDocumentStore<RecoveryState>(paths.RecoveryStateFile, paths.BackupDirectory, options, RecoveryValidator.ValidateState);
    }

    public async Task<RecoveryStartupResult> LoadForStartupAsync(CancellationToken cancellationToken = default)
    {
        var abnormal = File.Exists(_paths.ExitMarkerFile);
        var state = abnormal ? await _store.LoadAsync(cancellationToken) : null;
        var draft = abnormal ? await _drafts.LoadAsync(cancellationToken) : null;
        var marker = await MarkRunningAsync(cancellationToken);
        return new RecoveryStartupResult
        {
            AbnormalExit = abnormal,
            State = state,
            Draft = draft,
            MarkerError = marker.Success ? null : marker
        };
    }

    public Task<DocumentSaveResult> SaveAsync(RecoveryState state, string? expectedRevision, CancellationToken cancellationToken = default) =>
        _store.SaveAsync(state, expectedRevision, cancellationToken: cancellationToken);

    public async Task<DocumentSaveResult> DiscardAsync(CancellationToken cancellationToken = default)
    {
        var draftResult = await _drafts.DeleteAsync(cancellationToken);
        if (!draftResult.Success)
        {
            return draftResult;
        }
        return await DeleteFileAsync(_paths.RecoveryStateFile, cancellationToken);
    }

    public async Task<DocumentSaveResult> CompleteNormalShutdownAsync(CancellationToken cancellationToken = default)
    {
        var stateResult = await DeleteFileAsync(_paths.RecoveryStateFile, cancellationToken);
        if (!stateResult.Success)
        {
            return stateResult;
        }
        return await DeleteFileAsync(_paths.ExitMarkerFile, cancellationToken);
    }

    private async Task<DocumentSaveResult> MarkRunningAsync(CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllTextAsync(_paths.ExitMarkerFile, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.IoError, Error = $"{_paths.ExitMarkerFile}{Environment.NewLine}{exception.Message}" };
        }
    }

    private static async Task<DocumentSaveResult> DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }, cancellationToken);
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.IoError, Error = $"{path}{Environment.NewLine}{exception.Message}" };
        }
    }
}
