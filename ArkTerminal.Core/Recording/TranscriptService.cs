using System.Text;
using System.Threading.Channels;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Core.Recording;

public sealed record TranscriptStatus(bool Recording, bool Truncated, string Message);

public sealed class TranscriptService : IAsyncDisposable
{
    private readonly string _directory;
    private readonly Dictionary<Guid, Writer> _sessions = [];
    private readonly object _sync = new();
    private readonly SemaphoreSlim _files = new(1, 1);
    private readonly SemaphoreSlim _operations = new(1, 1);
    public event Action<Guid>? Changed;
    public TranscriptService(IAppDataPaths paths) => _directory = paths.SessionSavesDirectory;
    public TranscriptStatus GetStatus(Guid session)
    {
        lock (_sync) return _sessions.TryGetValue(session, out var writer) ? writer.Status : new(false, false, Localize.T("Text0674"));
    }

    public async Task StartAsync(Guid session, TranscriptionPolicy policy)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync(session).ConfigureAwait(false);
            var writer = new Writer(this, session, policy.DeepClone());
            await writer.OpenAsync().ConfigureAwait(false);
            lock (_sync) _sessions[session] = writer;
            writer.Start();
            Changed?.Invoke(session);
        }
        finally { _operations.Release(); }
    }
    public void TryAppend(Guid session, string text)
    {
        Writer? writer;
        lock (_sync) _sessions.TryGetValue(session, out writer);
        writer?.Append(text);
    }
    public async Task StopAsync(Guid session)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync(session).ConfigureAwait(false); }
        finally { _operations.Release(); }
    }
    private async Task StopCoreAsync(Guid session)
    {
        Writer? writer;
        lock (_sync) _sessions.TryGetValue(session, out writer);
        if (writer is not null) await writer.StopAsync().ConfigureAwait(false);
    }
    public async Task ClearAsync(Guid? session = null)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            Guid[] targets;
            lock (_sync) targets = _sessions.Keys.Where(id => session is null || id == session).ToArray();
            foreach (var target in targets) await StopCoreAsync(target).ConfigureAwait(false);
            await _files.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    foreach (var file in RecordingFiles.Transcripts(_directory).Where(file => session is null || file.Name.StartsWith("ark-output-" + session.Value.ToString("N") + "-", StringComparison.Ordinal))) file.Delete();
                }).ConfigureAwait(false);
            }
            finally { _files.Release(); }
        }
        finally { _operations.Release(); }
        Changed?.Invoke(session ?? Guid.Empty);
    }
    public async Task MaintainAsync(TranscriptionPolicy policy)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            await _files.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    var files = RecordingFiles.Transcripts(_directory).OrderBy(file => file.LastWriteTimeUtc).ToList();
                    foreach (var file in files.ToArray())
                    {
                        bool active;
                        lock (_sync) active = _sessions.Values.Any(writer => writer.Status.Recording && writer.Path == file.FullName);
                        if (!active && (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-policy.RetentionDays) || files.Sum(RecordedLength) > policy.MaxTotalSizeBytes || files.Count > RecordingLimits.MaxTranscriptFiles))
                        { file.Delete(); files.Remove(file); }
                    }
                }).ConfigureAwait(false);
            }
            finally { _files.Release(); }
        }
        finally { _operations.Release(); }
    }
    private void EnforceQuota(Writer writer, long incoming)
    {
        var files = RecordingFiles.Transcripts(_directory).OrderBy(file => file.LastWriteTimeUtc).ToList();
        var ownPrefix = "ark-output-" + writer.Session.ToString("N") + "-";
        foreach (var file in files.ToArray())
        {
            if (file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-writer.Policy.RetentionDays) && !ActiveFile(file.FullName, writer))
            { file.Delete(); files.Remove(file); }
        }
        while (files.Count > 0 && (files.Sum(RecordedLength) + incoming > writer.Policy.MaxTotalSizeBytes ||
            files.Where(file => file.Name.StartsWith(ownPrefix, StringComparison.Ordinal)).Sum(RecordedLength) + incoming > writer.Policy.MaxFileSizeBytes || files.Count >= RecordingLimits.MaxTranscriptFiles))
        {
            var victim = files.FirstOrDefault(file => !ActiveFile(file.FullName, writer) &&
                (files.Sum(RecordedLength) + incoming > writer.Policy.MaxTotalSizeBytes || files.Count >= RecordingLimits.MaxTranscriptFiles || file.Name.StartsWith(ownPrefix, StringComparison.Ordinal)));
            if (victim is null) throw new IOException(Localize.T("Text0675"));
            victim.Delete(); files.Remove(victim); writer.Truncated = true;
        }
    }
    private bool ActiveFile(string path, Writer current)
    {
        if (path == current.Path) return true;
        lock (_sync) return _sessions.Values.Any(writer => writer.Status.Recording && writer.Path == path);
    }
    private long RecordedLength(FileInfo file)
    {
        // NTFS directory enumeration may report an old size while a file is open.
        lock (_sync)
        {
            var writer = _sessions.Values.FirstOrDefault(item => item.Path == file.FullName);
            return writer is null ? file.Length : Math.Max(file.Length, writer.CurrentLength);
        }
    }
    public async Task ForgetAsync(Guid session)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync(session).ConfigureAwait(false); lock (_sync) _sessions.Remove(session); }
        finally { _operations.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        Guid[] sessions;
        lock (_sync) sessions = _sessions.Keys.ToArray();
        foreach (var session in sessions) await StopAsync(session).ConfigureAwait(false);
        _files.Dispose(); _operations.Dispose();
    }

    private sealed class Writer(TranscriptService owner, Guid session, TranscriptionPolicy policy)
    {
        private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(RecordingLimits.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        private volatile bool _accepting;
        private volatile bool _truncated;
        private string _message = Localize.T("Text0674");
        private Task _worker = Task.CompletedTask;
        private FileStream? _stream;
        private long _length;
        internal long CurrentLength => Interlocked.Read(ref _length);
        internal Guid Session { get; } = session;
        internal TranscriptionPolicy Policy { get; } = policy;
        internal string Path { get; private set; } = string.Empty;
        internal bool Truncated
        {
            get => _truncated;
            set { var previous = _truncated; _truncated = value; if (value && !previous) owner.Changed?.Invoke(Session); }
        }
        internal TranscriptStatus Status => new(_accepting, _truncated, _message);
        internal async Task OpenAsync()
        {
            await owner._files.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(async () =>
                {
                    RecordingFiles.CheckDirectory(owner._directory);
                    Path = System.IO.Path.Combine(owner._directory, "ark-output-" + Session.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".txt");
                    owner.EnforceQuota(this, 256);
                    _stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 4096, true);
                    var header = Encoding.UTF8.GetBytes(Localize.T("Text0676"));
                    await _stream.WriteAsync(header).ConfigureAwait(false);
                    Interlocked.Exchange(ref _length, header.Length);
                    await _stream.FlushAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch { if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false); throw; }
            finally { owner._files.Release(); }
        }
        internal void Start() { _accepting = true; _message = Localize.T("Text0677"); _worker = Task.Run(RunAsync); }
        internal void Append(string text)
        {
            if (!_accepting || text.Length == 0) return;
            if (text.Length <= RecordingLimits.MaxTextChunkCharacters && _queue.Writer.TryWrite(text)) return;
            _truncated = true; _accepting = false; _message = Localize.T("Text0678");
            _queue.Writer.TryComplete(); owner.Changed?.Invoke(Session);
        }
        internal async Task StopAsync()
        {
            _accepting = false; _queue.Writer.TryComplete();
            await _worker.ConfigureAwait(false);
            if (!_message.Contains(Localize.T("Text0161")) && !_message.Contains(Localize.T("Text0679"))) _message = _truncated ? Localize.T("Text0680") : Localize.T("Text0681");
            owner.Changed?.Invoke(Session);
        }
        private async Task RunAsync()
        {
            try
            {
                await foreach (var text in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    if (!_accepting) continue;
                    var bytes = Encoding.UTF8.GetBytes(text);
                    // Split at UTF-8 boundaries; each segment uses at most half the session budget.
                    var offset = 0;
                    while (_accepting && offset < bytes.Length)
                    {
                        await owner._files.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            var segmentLimit = Math.Max(512, Policy.MaxFileSizeBytes / 2);
                            if (_stream!.Length >= segmentLimit - 4)
                            {
                                await _stream.DisposeAsync().ConfigureAwait(false);
                                Path = System.IO.Path.Combine(owner._directory, "ark-output-" + Session.ToString("N") + "-" + Guid.NewGuid().ToString("N") + ".txt");
                                Interlocked.Exchange(ref _length, 0);
                                var header = Encoding.UTF8.GetBytes(Localize.T("Text0682"));
                                owner.EnforceQuota(this, header.Length);
                                _stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 4096, true);
                                await _stream.WriteAsync(header).ConfigureAwait(false);
                                Interlocked.Exchange(ref _length, header.Length);
                                await _stream.FlushAsync().ConfigureAwait(false);
                                Truncated = true;
                            }
                            var count = (int)Math.Min(bytes.Length - offset, segmentLimit - _stream.Length);
                            while (count > 0 && offset + count < bytes.Length && (bytes[offset + count] & 0xc0) == 0x80) count--;
                            owner.EnforceQuota(this, count);
                            if (_accepting && count > 0)
                            {
                                await _stream.WriteAsync(bytes.AsMemory(offset, count)).ConfigureAwait(false);
                                Interlocked.Add(ref _length, count);
                            }
                            offset += count;
                        }
                        finally { owner._files.Release(); }
                    }
                    if (_accepting) await _stream!.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { _accepting = false; _message = Localize.T("Text0683"); Truncated = true; owner.Changed?.Invoke(Session); }
            finally
            {
                try { if (_stream is not null) await _stream.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { _accepting = false; _message = Localize.T("Text0683"); Truncated = true; owner.Changed?.Invoke(Session); }
            }
        }
    }
}
