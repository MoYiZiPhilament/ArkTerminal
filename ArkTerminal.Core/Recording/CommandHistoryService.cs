using System.Text.Json;
using System.Threading.Channels;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Core.Recording;

public sealed record CommandHistoryEntry(DateTimeOffset TimeUtc, string Shell, string Command, string? WorkingDirectory,
    int? ExitCode, bool? Succeeded, double DurationMilliseconds);

public sealed class CommandHistoryService : IAsyncDisposable
{
    private readonly string _path;
    private readonly Channel<(long Consent, CommandHistoryEntry Entry)> _queue = Channel.CreateBounded<(long, CommandHistoryEntry)>(
        new BoundedChannelOptions(RecordingLimits.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim _files = new(1, 1);
    private readonly object _sync = new();
    private readonly Task _writer;
    private HistoryPolicy _policy = new();
    private long _consent;
    private bool _enabled;
    private string _status = "应用历史已关闭；只使用 Shell 自身历史。";

    public CommandHistoryService(IAppDataPaths paths)
    {
        _path = Path.Combine(paths.HistoryDirectory, "ark-command-history.json");
        _writer = Task.Run(WriteAsync);
    }
    public bool Enabled { get { lock (_sync) return _enabled; } }
    public long ConsentVersion { get { lock (_sync) return _enabled ? _consent : -1; } }
    public string Status { get { lock (_sync) return _status; } }
    public event Action? Changed;

    public async Task SetEnabledAsync(bool enabled, HistoryPolicy policy)
    {
        lock (_sync)
        {
            _enabled = enabled;
            _policy = policy.DeepClone();
            _consent++;
            _status = enabled ? "正在记录本次运行中有可靠开始与结束事件的命令；重启后默认关闭。" : "应用历史已关闭。";
        }
        // Barrier: after stopping returns, no previous consent can still write.
        await _files.WaitAsync().ConfigureAwait(false);
        _files.Release();
        Changed?.Invoke();
    }

    public void TryRecord(long consent, CommandHistoryEntry entry)
    {
        lock (_sync)
        {
            if (!_enabled || consent != _consent || string.IsNullOrWhiteSpace(entry.Command) || entry.Command.Length > RecordingLimits.MaxCommandCharacters) return;
            if (_queue.Writer.TryWrite((consent, entry))) return;
            _enabled = false; _consent++; _status = "历史队列已满，已停止记录；终端继续运行。";
        }
        Changed?.Invoke();
    }

    public async Task<IReadOnlyList<CommandHistoryEntry>> ReadAsync(string query = "")
    {
        await _files.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Read().Where(item => item.Command.Contains(query, StringComparison.OrdinalIgnoreCase)).Reverse().ToArray()).ConfigureAwait(false);
        }
        finally { _files.Release(); }
    }

    public async Task ClearAsync()
    {
        lock (_sync) _consent++;
        await _files.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => { RecordingFiles.CheckDirectory(Path.GetDirectoryName(_path)!); RecordingFiles.CheckFile(_path); File.Delete(_path); }).ConfigureAwait(false);
        }
        finally { _files.Release(); }
        Changed?.Invoke();
    }

    private List<CommandHistoryEntry> Read()
    {
        RecordingFiles.CheckDirectory(Path.GetDirectoryName(_path)!);
        RecordingFiles.CheckFile(_path);
        if (!File.Exists(_path)) return [];
        if (new FileInfo(_path).Length > RecordingLimits.MaxReadHistoryBytes) throw new IOException("历史文件超出读取上限。");
        var document = JsonSerializer.Deserialize<HistoryDocument>(File.ReadAllBytes(_path)) ?? throw new IOException("历史文件无效。");
        if (document.SchemaVersion != 1) throw new IOException("历史版本不受支持，未覆盖原文件。");
        if (document.Entries is null) throw new IOException("历史条目无效。");
        return document.Entries.Where(item => item is not null && item.Command is { Length: <= RecordingLimits.MaxCommandCharacters }).ToList();
    }

    private async Task WriteAsync()
    {
        await foreach (var item in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await _files.WaitAsync().ConfigureAwait(false);
            string? temporary = null;
            try
            {
                HistoryPolicy policy;
                lock (_sync) { if (!_enabled || item.Consent != _consent) continue; policy = _policy; }
                var entries = Read();
                entries.RemoveAll(entry => entry.TimeUtc < DateTimeOffset.UtcNow.AddDays(-policy.RetentionDays));
                entries.Add(item.Entry);
                if (entries.Count > policy.MaxEntries) entries.RemoveRange(0, entries.Count - policy.MaxEntries);
                byte[] bytes;
                while ((bytes = JsonSerializer.SerializeToUtf8Bytes(new HistoryDocument { Entries = entries })).Length > policy.MaxSizeBytes && entries.Count > 0)
                    entries.RemoveAt(0);
                temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                lock (_sync) { if (!_enabled || item.Consent != _consent) continue; }
                RecordingFiles.CheckFile(_path);
                File.Move(temporary, _path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                lock (_sync) { _enabled = false; _consent++; _status = "历史写入失败，已停止记录；终端继续运行。"; }
                Changed?.Invoke();
            }
            finally
            {
                if (temporary is not null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                _files.Release();
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await SetEnabledAsync(false, _policy).ConfigureAwait(false);
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
        _files.Dispose();
    }
    private sealed class HistoryDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<CommandHistoryEntry> Entries { get; set; } = [];
    }
}
