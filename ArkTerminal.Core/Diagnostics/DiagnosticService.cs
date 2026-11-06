using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Threading.Channels;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Core.Diagnostics;

public enum DiagnosticComponent { Startup, Configuration, Profiles, Terminal, Rendering, Visuals, Diagnostics, Context }
public enum DiagnosticEventKind { ReadFailed, WriteFailed, StartFailed, InputFailed, RenderFailed, ImportFailed, CleanupFailed, ContextPrepared, ContextCopied, SnapshotSaved, CommandSubmitted, McpReadAllowed, McpReadDenied, McpGrantChanged, ContextCancelled, ContextFailed }
public sealed record DiagnosticEvent(DateTimeOffset TimeUtc, DiagnosticComponent Component, DiagnosticEventKind Event, int ErrorCode, int? SessionSlot);

public sealed class DiagnosticsOptions
{
    public int QueueCapacity { get; init; } = 256;
    public int MaxLogBytes { get; init; } = 512 * 1024;
    public int MaxLogFiles { get; init; } = 3;
    public int MaxExportBytes { get; init; } = 1024 * 1024;
    public int MaxExportFiles { get; init; } = 5;
    public int RetentionDays { get; init; } = 7;
}

public sealed class DiagnosticService : IAsyncDisposable
{
    private const string LogPrefix = "ark-events-";
    private const string ExportPrefix = "ark-diagnostics-";
    private readonly string _directory;
    private readonly Channel<(int Generation, DiagnosticEvent Event)> _events;
    private readonly SemaphoreSlim _files = new(1, 1);
    private readonly Task _writer;
    private string? _currentLog;
    private long _dropped;
    private long _writeFailures;
    private readonly object _recentSync = new();
    private readonly Queue<DiagnosticEvent> _recent = new();
    private int _generation;

    public DiagnosticService(IAppDataPaths paths, DiagnosticsOptions? options = null)
    {
        _directory = paths.DiagnosticsDirectory;
        Options = options ?? new();
        _events = Channel.CreateBounded<(int Generation, DiagnosticEvent Event)>(new BoundedChannelOptions(Options.QueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        _writer = Task.Run(WriteEventsAsync);
    }

    public DiagnosticsOptions Options { get; }
    public long DroppedEvents => Interlocked.Read(ref _dropped);
    public long WriteFailures => Interlocked.Read(ref _writeFailures);

    // This API deliberately cannot accept exception messages or user-controlled text.
    public void Record(DiagnosticComponent component, DiagnosticEventKind kind, int errorCode = 0, int? sessionSlot = null)
    {
        var item = new DiagnosticEvent(DateTimeOffset.UtcNow, component, kind, errorCode, sessionSlot);
        int generation;
        lock (_recentSync)
        {
            generation = _generation;
            _recent.Enqueue(item);
            while (_recent.Count > Options.QueueCapacity) _recent.Dequeue();
        }
        if (!_events.Writer.TryWrite((generation, item)))
            Interlocked.Increment(ref _dropped);
    }

    public async Task<IReadOnlyList<DiagnosticEvent>> ReadRecentAsync(CancellationToken token = default)
    {
        return await Task.Run(async () =>
        {
            var result = new List<DiagnosticEvent>();
            await _files.WaitAsync(token);
            try
            {
                foreach (var file in OwnedFiles(LogPrefix, ".log").OrderByDescending(file => file.Name).Take(Options.MaxLogFiles))
                {
                    if (file.Length > Options.MaxLogBytes) continue;
                    var lines = await File.ReadAllLinesAsync(file.FullName, token);
                    foreach (var line in lines.TakeLast(256))
                    {
                        try
                        {
                            var item = JsonSerializer.Deserialize<DiagnosticEvent>(line);
                            if (item is not null && Enum.IsDefined(item.Component) && Enum.IsDefined(item.Event)) result.Add(item);
                        }
                        catch (JsonException) { }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Interlocked.Increment(ref _writeFailures); }
            finally { _files.Release(); }
            lock (_recentSync) result.AddRange(_recent);
            return (IReadOnlyList<DiagnosticEvent>)result.Distinct().OrderBy(item => item.TimeUtc).TakeLast(256).ToArray();
        }, token);
    }

    public async Task<string> ExportAsync(string confirmedText, CancellationToken token = default)
    {
        var bytes = Encoding.UTF8.GetBytes(confirmedText);
        if (bytes.Length > Options.MaxExportBytes) throw new InvalidOperationException(Localize.T("Text0651"));
        return await Task.Run(async () =>
        {
            await _files.WaitAsync(token);
            var path = Path.Combine(_directory, ExportPrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token);
                File.Move(temporary, path);
                try { Prune(ExportPrefix, ".txt", Options.MaxExportFiles); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref _writeFailures); }
                return path;
            }
            finally
            {
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                _files.Release();
            }
        }, token);
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        await Task.Run(async () =>
        {
            await _files.WaitAsync(token);
            try
            {
                foreach (var file in OwnedFiles(LogPrefix, ".log").Concat(OwnedFiles(ExportPrefix, ".txt"))) file.Delete();
                _currentLog = null;
                lock (_recentSync) { _generation++; _recent.Clear(); }
            }
            finally { _files.Release(); }
        }, token);
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
        _files.Dispose();
    }

    private async Task WriteEventsAsync()
    {
        await _files.WaitAsync();
        try
        {
            Prune(LogPrefix, ".log", Options.MaxLogFiles);
            Prune(ExportPrefix, ".txt", Options.MaxExportFiles);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref _writeFailures); }
        finally { _files.Release(); }
        await foreach (var queued in _events.Reader.ReadAllAsync())
        {
            await _files.WaitAsync();
            try
            {
                if (queued.Generation != Volatile.Read(ref _generation)) continue;
                var text = JsonSerializer.Serialize(queued.Event) + "\n";
                var length = Encoding.UTF8.GetByteCount(text);
                if (_currentLog is null || !File.Exists(_currentLog) || new FileInfo(_currentLog).Length + length > Options.MaxLogBytes)
                {
                    _currentLog = Path.Combine(_directory, LogPrefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".log");
                }
                await File.AppendAllTextAsync(_currentLog, text, new UTF8Encoding(false));
                Prune(LogPrefix, ".log", Options.MaxLogFiles);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { Interlocked.Increment(ref _writeFailures); _currentLog = null; }
            finally { _files.Release(); }
        }
    }

    private IEnumerable<FileInfo> OwnedFiles(string prefix, string extension) =>
        new DirectoryInfo(_directory).EnumerateFiles(prefix + "*" + extension, SearchOption.TopDirectoryOnly)
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0 && IsOwnedName(file.Name, prefix, extension));

    private static bool IsOwnedName(string name, string prefix, string extension)
    {
        var suffix = name[prefix.Length..^extension.Length];
        return suffix.Length == 56 && suffix[23] == '-' && Guid.TryParseExact(suffix[24..], "N", out _) &&
            DateTime.TryParseExact(suffix[..23], "yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private void Prune(string prefix, string extension, int maximum)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Options.RetentionDays);
        var files = OwnedFiles(prefix, extension).OrderByDescending(file => file.Name).ToArray();
        for (var index = 0; index < files.Length; index++)
            if (index >= maximum || files[index].LastWriteTimeUtc < cutoff) files[index].Delete();
    }
}
