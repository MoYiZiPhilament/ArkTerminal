using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Storage;

public enum DocumentLoadStatus
{
    Success,
    NotFound,
    Corrupted,
    Invalid,
    UnsupportedNewerVersion,
    TooLarge,
    IoError
}

public enum DocumentSaveStatus
{
    Success,
    Conflict,
    Invalid,
    UnsupportedNewerVersion,
    IoError
}

public sealed class DocumentLoadResult<T>
{
    public required DocumentLoadStatus Status { get; init; }
    public T? Value { get; init; }
    public string? Revision { get; init; }
    public string? Error { get; init; }
    public string? BackupPath { get; init; }
    public bool Success => Status == DocumentLoadStatus.Success;
}

public sealed class DocumentSaveResult
{
    public required DocumentSaveStatus Status { get; init; }
    public string? Revision { get; init; }
    public string? Error { get; init; }
    public ValidationResult? Validation { get; init; }
    public bool Success => Status == DocumentSaveStatus.Success;
}

public sealed class StorageOptions
{
    public long MaxDocumentBytes { get; init; } = 1024 * 1024;
    public int MaxBackupsPerDocument { get; init; } = 5;
    public int DraftDebounceMilliseconds { get; init; } = 500;
}
