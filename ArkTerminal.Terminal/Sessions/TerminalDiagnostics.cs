namespace ArkTerminal.Terminal.Sessions;

public sealed class TerminalDiagnosticSnapshot
{
    public long BytesRead { get; init; }
    public long ReplacementCharacters { get; init; }
    public long IgnoredSequences { get; init; }
    public long TruncatedSequences { get; init; }
    public long CsiSequences { get; init; }
    public long OscSequences { get; init; }
    public long InputBytesWritten { get; init; }
    public bool ReadWorkerActive { get; init; }
    public bool WriteWorkerActive { get; init; }
    public int InputQueueDepth { get; init; }
    public int InputQueueCapacity { get; init; }
    public bool ResizePending { get; init; }
}

internal sealed class TerminalDiagnostics
{
    private long _bytesRead;
    private long _replacementCharacters;
    private long _ignoredSequences;
    private long _truncatedSequences;
    private long _csiSequences;
    private long _oscSequences;
    private long _inputBytesWritten;
    private int _readWorkerActive;
    private int _writeWorkerActive;

    internal void AddBytesRead(int count) => Interlocked.Add(ref _bytesRead, count);
    internal void AddReplacement() => Interlocked.Increment(ref _replacementCharacters);
    internal void AddIgnoredSequence() => Interlocked.Increment(ref _ignoredSequences);
    internal void AddTruncatedSequence() => Interlocked.Increment(ref _truncatedSequences);
    internal void AddCsi() => Interlocked.Increment(ref _csiSequences);
    internal void AddOsc() => Interlocked.Increment(ref _oscSequences);
    internal void AddInputBytes(int count) => Interlocked.Add(ref _inputBytesWritten, count);
    internal void SetReadWorker(bool active) => Interlocked.Exchange(ref _readWorkerActive, active ? 1 : 0);
    internal void SetWriteWorker(bool active) => Interlocked.Exchange(ref _writeWorkerActive, active ? 1 : 0);

    public TerminalDiagnosticSnapshot Snapshot(int queueDepth = 0, int queueCapacity = 0, bool resizePending = false) => new()
    {
        InputQueueDepth = queueDepth,
        InputQueueCapacity = queueCapacity,
        ResizePending = resizePending,
        BytesRead = Interlocked.Read(ref _bytesRead),
        ReplacementCharacters = Interlocked.Read(ref _replacementCharacters),
        IgnoredSequences = Interlocked.Read(ref _ignoredSequences),
        TruncatedSequences = Interlocked.Read(ref _truncatedSequences),
        CsiSequences = Interlocked.Read(ref _csiSequences),
        OscSequences = Interlocked.Read(ref _oscSequences),
        InputBytesWritten = Interlocked.Read(ref _inputBytesWritten),
        ReadWorkerActive = _readWorkerActive != 0,
        WriteWorkerActive = _writeWorkerActive != 0
    };
}
