using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Storage;

public interface IJsonDocumentMigrator
{
    JsonObject Migrate(JsonObject document, int sourceVersion);
}

public sealed class JsonDocumentStore<T>
{
    private readonly string _path;
    private readonly string _backupDirectory;
    private readonly StorageOptions _options;
    private readonly Func<T, ValidationResult> _validate;
    private readonly IJsonDocumentMigrator? _migrator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonDocumentStore(
        string path,
        string backupDirectory,
        StorageOptions options,
        Func<T, ValidationResult> validate,
        IJsonDocumentMigrator? migrator = null)
    {
        _path = path;
        _backupDirectory = backupDirectory;
        _options = options;
        _validate = validate;
        _migrator = migrator;
    }

    public async Task<DocumentLoadResult<T>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new DocumentLoadResult<T> { Status = DocumentLoadStatus.NotFound };
            }

            byte[] bytes;
            try
            {
                var length = new FileInfo(_path).Length;
                if (length > _options.MaxDocumentBytes)
                {
                    return new DocumentLoadResult<T> { Status = DocumentLoadStatus.TooLarge, Error = Localize.F("Text0692", _options.MaxDocumentBytes, _path) };
                }
                bytes = await File.ReadAllBytesAsync(_path, cancellationToken);
            }
            catch (Exception exception) when (IsIo(exception))
            {
                return IoLoadError(exception);
            }

            var revision = ComputeRevision(bytes);
            JsonObject root;
            try
            {
                root = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32
                }) as JsonObject ?? throw new JsonException(Localize.T("Text0554"));
            }
            catch (JsonException exception)
            {
                return await CorruptLoadResultAsync(bytes, Localize.F("Text0693", exception.Message), cancellationToken);
            }

            if (!TryGetSchemaVersion(root, out var version))
            {
                return await CorruptLoadResultAsync(bytes, Localize.T("Text0694"), cancellationToken);
            }

            if (version > ConfigurationDefaults.CurrentSchemaVersion)
            {
                return new DocumentLoadResult<T>
                {
                    Status = DocumentLoadStatus.UnsupportedNewerVersion,
                    Revision = revision,
                    Error = Localize.F("Text0695", version, ConfigurationDefaults.CurrentSchemaVersion)
                };
            }

            if (version < 1 || version < ConfigurationDefaults.CurrentSchemaVersion && _migrator is null)
            {
                return await CorruptLoadResultAsync(bytes, Localize.F("Text0696", version), cancellationToken);
            }

            try
            {
                if (version < ConfigurationDefaults.CurrentSchemaVersion)
                {
                    root = _migrator!.Migrate(root, version);
                }

                var value = root.Deserialize<T>(_serializerOptions);
                var validation = _validate(value!);
                if (value is null || !validation.IsValid)
                {
                    return await CorruptLoadResultAsync(bytes, validation.Summary, cancellationToken);
                }

                return new DocumentLoadResult<T>
                {
                    Status = DocumentLoadStatus.Success,
                    Value = value,
                    Revision = revision
                };
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                return await CorruptLoadResultAsync(bytes, Localize.F("Text0697", exception.Message), cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DocumentSaveResult> SaveAsync(
        T value,
        string? expectedRevision,
        bool replaceInvalid = false,
        CancellationToken cancellationToken = default)
    {
        var validation = _validate(value);
        if (!validation.IsValid)
        {
            return new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Invalid,
                Validation = validation,
                Error = validation.Summary
            };
        }

        await _gate.WaitAsync(cancellationToken);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(_path)!, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            if (File.Exists(_path))
            {
                byte[] current;
                try
                {
                    if (new FileInfo(_path).Length > _options.MaxDocumentBytes)
                    {
                        return new DocumentSaveResult { Status = DocumentSaveStatus.Invalid, Error = Localize.T("Text0698") };
                    }
                    current = await File.ReadAllBytesAsync(_path, cancellationToken);
                }
                catch (Exception exception) when (IsIo(exception))
                {
                    return IoSaveError(exception);
                }

                var currentRevision = ComputeRevision(current);
                if (expectedRevision is not null && !string.Equals(expectedRevision, currentRevision, StringComparison.Ordinal))
                {
                    return new DocumentSaveResult { Status = DocumentSaveStatus.Conflict, Error = Localize.T("Text0699") };
                }

                var protection = await CheckOverwriteProtectionAsync(current, replaceInvalid, cancellationToken);
                if (protection is not null)
                {
                    return protection;
                }
            }
            else if (!string.IsNullOrEmpty(expectedRevision))
            {
                return new DocumentSaveResult { Status = DocumentSaveStatus.Conflict, Error = Localize.T("Text0700") };
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, _serializerOptions);
            if (bytes.Length > _options.MaxDocumentBytes)
            {
                return new DocumentSaveResult { Status = DocumentSaveStatus.Invalid, Error = Localize.F("Text0701", _options.MaxDocumentBytes) };
            }

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(true);
                }

                await Task.Run(() =>
                {
                    if (File.Exists(_path))
                    {
                        File.Replace(temporaryPath, _path, null, true);
                    }
                    else
                    {
                        File.Move(temporaryPath, _path);
                    }
                }, cancellationToken);
            }
            catch (Exception exception) when (IsIo(exception))
            {
                return IoSaveError(exception);
            }

            return new DocumentSaveResult { Status = DocumentSaveStatus.Success, Revision = ComputeRevision(bytes) };
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    await Task.Run(() => File.Delete(temporaryPath), CancellationToken.None);
                }
            }
            catch
            {
            }
            _gate.Release();
        }
    }

    private async Task<DocumentSaveResult?> CheckOverwriteProtectionAsync(byte[] current, bool replaceInvalid, CancellationToken cancellationToken)
    {
        try
        {
            var root = JsonNode.Parse(current) as JsonObject;
            if (root is null || !TryGetSchemaVersion(root, out var version))
            {
                return replaceInvalid ? null : new DocumentSaveResult { Status = DocumentSaveStatus.Invalid, Error = Localize.T("Text0702") };
            }
            if (version > ConfigurationDefaults.CurrentSchemaVersion)
            {
                return new DocumentSaveResult
                {
                    Status = DocumentSaveStatus.UnsupportedNewerVersion,
                    Error = Localize.T("Text0703")
                };
            }
            if (!replaceInvalid)
            {
                _ = root.Deserialize<T>(_serializerOptions);
            }
            return null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            return replaceInvalid ? null : new DocumentSaveResult
            {
                Status = DocumentSaveStatus.Invalid,
                Error = Localize.F("Text0704", exception.Message)
            };
        }
    }

    private async Task<DocumentLoadResult<T>> CorruptLoadResultAsync(byte[] bytes, string error, CancellationToken cancellationToken)
    {
        string? backupPath = null;
        try
        {
            Directory.CreateDirectory(_backupDirectory);
            backupPath = Path.Combine(
                _backupDirectory,
                $"{Path.GetFileNameWithoutExtension(_path)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(_path)}");
            await File.WriteAllBytesAsync(backupPath, bytes, cancellationToken);
            await PruneBackupsAsync(cancellationToken);
        }
        catch (Exception exception) when (IsIo(exception))
        {
            error = Localize.F("Text0705", error, Environment.NewLine, exception.Message);
            backupPath = null;
        }

        return new DocumentLoadResult<T>
        {
            Status = DocumentLoadStatus.Corrupted,
            Error = $"{_path}{Environment.NewLine}{error}",
            BackupPath = backupPath
        };
    }

    private async Task PruneBackupsAsync(CancellationToken cancellationToken)
    {
        var prefix = $"{Path.GetFileNameWithoutExtension(_path)}-";
        var extension = Path.GetExtension(_path);
        var obsolete = new DirectoryInfo(_backupDirectory)
            .EnumerateFiles()
            .Where(file => file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Skip(_options.MaxBackupsPerDocument)
            .ToArray();
        foreach (var file in obsolete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => file.Delete(), cancellationToken);
        }
    }

    private static bool TryGetSchemaVersion(JsonObject root, out int version)
    {
        version = 0;
        return root.TryGetPropertyValue("schemaVersion", out var node) && node is JsonValue value && value.TryGetValue(out version);
    }

    private static string ComputeRevision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static bool IsIo(Exception exception) => exception is IOException or UnauthorizedAccessException or NotSupportedException;

    private DocumentLoadResult<T> IoLoadError(Exception exception) => new()
    {
        Status = DocumentLoadStatus.IoError,
        Error = $"{_path}{Environment.NewLine}{exception.Message}"
    };

    private DocumentSaveResult IoSaveError(Exception exception) => new()
    {
        Status = DocumentSaveStatus.IoError,
        Error = $"{_path}{Environment.NewLine}{exception.Message}"
    };
}
