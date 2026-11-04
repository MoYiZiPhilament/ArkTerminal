using System.Threading.Channels;
using ArkTerminal.Terminal.Decoding;
using ArkTerminal.Terminal.Rendering;
using ArkTerminal.Terminal.Screen;
using ArkTerminal.Terminal.Sessions.Windows;
using Microsoft.Win32.SafeHandles;
using ArkTerminal.Terminal.Integration;
using ArkTerminal.Terminal.Sessions.Elevation;
using System.Text;

namespace ArkTerminal.Terminal.Sessions;

public sealed class ConPtySession : ISession, ITerminalBufferReader, ITerminalInput, ITerminalBufferActions, IShellIntegrationSource, ITerminalTextSource
{
    private readonly object _lifecycleSync = new();
    private readonly object _resizeSync = new();
    private readonly TerminalSessionOptions _options;
    private readonly TerminalScreen _screen;
    private readonly TerminalStreamDecoder _streamDecoder;
    private readonly TerminalDiagnostics _diagnostics = new();
    private readonly ShellIntegrationTracker _shell = new();
    private readonly StringBuilder _plainText = new();
    private ElevatedConnection? _remote;
    private readonly Channel<ReadOnlyMemory<byte>> _input;
    private readonly CancellationTokenSource _writeCancellation = new();
    private readonly CancellationTokenSource _resizeCancellation = new();
    private readonly SemaphoreSlim _resizeSignal = new(0, 1);
    private ConPtyProcess? _process;
    private Task? _readTask;
    private Task? _writeTask;
    private Task? _resizeTask;
    private Task? _processExitTask;
    private Task? _closeTask;
    private SafeThreadHandle? _readThread;
    private SafeThreadHandle? _writeThread;
    private ResizeRequest? _pendingResize;
    private SessionState _state = SessionState.Created;
    private int _notificationPending;
    private bool _acceptingInput;
    private bool _disposed;

    internal ConPtySession(TerminalSessionOptions options)
    {
        _options = options;
        Id = Guid.NewGuid();
        _screen = new TerminalScreen(options.InitialColumns, options.InitialRows, options.MaxScrollbackLines, options.MaxScrollbackBytes);
        _streamDecoder = new TerminalStreamDecoder(_screen, options, WriteResponse, _shell.Accept, text => { if (TextReceived is not null) _plainText.Append(text); });
        _shell.Changed += (_, args) => ShellIntegrationChanged?.Invoke(this, args);
        _input = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(options.InputQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public Guid Id { get; }

    public SessionState State
    {
        get
        {
            lock (_lifecycleSync)
            {
                return _state;
            }
        }
    }

    public TerminalDiagnosticSnapshot Diagnostics
    {
        get
        {
            var io = _diagnostics.Snapshot(_input.Reader.Count, _options.InputQueueCapacity, _pendingResize is not null);
            var parser = _streamDecoder.Diagnostics;
            return new()
            {
                BytesRead = io.BytesRead, InputBytesWritten = io.InputBytesWritten, InputQueueDepth = io.InputQueueDepth, InputQueueCapacity = io.InputQueueCapacity,
                ResizePending = io.ResizePending, ReadWorkerActive = io.ReadWorkerActive, WriteWorkerActive = io.WriteWorkerActive,
                ReplacementCharacters = parser.ReplacementCharacters, IgnoredSequences = parser.IgnoredSequences, TruncatedSequences = parser.TruncatedSequences,
                CsiSequences = parser.CsiSequences, OscSequences = parser.OscSequences
            };
        }
    }
    public TerminalBufferStatistics BufferStatistics => _screen.GetStatistics();
    public string ShellTitle => _screen.GetShellTitle();
    public string? VerifiedLaunchProcessName { get; private set; }
    public ShellStatus ShellStatus => _shell.Status;
    public event EventHandler<ShellIntegrationEventArgs>? ShellIntegrationChanged;
    public event Action<string>? TextReceived;

    public void ClearViewport() { _screen.ClearViewport(); PublishScreenChanges(); }
    public void ClearScrollback() { _screen.ClearScrollback(); PublishScreenChanges(); }
    public bool BracketedPasteEnabled => _screen.GetModes().BracketedPaste;
    public bool ApplicationCursorKeys => _screen.GetModes().ApplicationCursorKeys;
    public bool Win32InputModeEnabled => _screen.GetModes().Win32InputMode;

    public event EventHandler<SessionOutputEventArgs>? OutputReceived;
    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<TerminalScreenChangedEventArgs>? SnapshotChanged;

    internal async Task StartAsync(SessionLaunchRequest request, CancellationToken cancellationToken)
    {
        TransitionTo(SessionState.Starting);
        ConPtyProcess? process = null;
        try
        {
            if (request.RunAsAdministrator)
                request = await Task.Run(() => ShellLaunchAdapter.ResolveRequest(request), cancellationToken).ConfigureAwait(false);
            var integration = ShellIntegrationInjector.Prepare(request);
            _shell.Configure(integration.Token, integration.Shell, integration.Capabilities, integration.Detail);
            request = integration.Request;
            if (request.RunAsAdministrator)
            {
                _remote = await ElevatedConnection.StartAsync(Id, request, _options, DecodeOutput, error =>
                {
                    _streamDecoder.Complete(); PublishPlainText(); PublishScreenChanges();
                    if (error is not null && State != SessionState.Exiting) TransitionTo(SessionState.Failed, error);
                    _ = Task.Run(() => CloseAsync(CancellationToken.None));
                }, cancellationToken).ConfigureAwait(false);
                _remote.BeginReading();
                lock (_lifecycleSync)
                {
                    _acceptingInput = _closeTask is null;
                    _readTask = _remote.Completion;
                    _writeTask = Task.Run(RemoteWriteLoopAsync);
                    _resizeTask = Task.Run(ResizeLoopAsync);
                }
            }
            else
            {
                process = await Task.Run(
                    () => ConPtyProcess.Start(request, _options.InitialColumns, _options.InitialRows),
                    cancellationToken).ConfigureAwait(false);
                lock (_lifecycleSync)
                {
                    if (_closeTask is not null)
                    {
                        process.Dispose();
                        return;
                    }
                    _process = process;
                    VerifiedLaunchProcessName = process.ExecutableName;
                    _acceptingInput = true;
                    // Synchronous ConPTY pipe I/O must not occupy shared pool workers while idle.
                    _readTask = Task.Factory.StartNew(ReadLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    _writeTask = Task.Factory.StartNew(WriteLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    _resizeTask = Task.Run(ResizeLoopAsync);
                    _processExitTask = WatchProcessExitAsync(process);
                }
            }
            if (State == SessionState.Starting)
            {
                TransitionTo(SessionState.Running);
            }
            if (!string.IsNullOrEmpty(request.PostLaunchInput))
            {
                await WriteInputAsync(request.PostLaunchInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (_process is null)
            {
                process?.Dispose();
            }
            TransitionTo(SessionState.Failed);
            throw;
        }
    }

    public TerminalScreenSnapshot GetSnapshot() => _screen.GetSnapshot(Id);
    public TerminalScreenSnapshot GetSnapshot(int scrollbackOffset) => _screen.GetSnapshot(Id, scrollbackOffset);
    public TerminalBufferTextSnapshot CaptureText() => _screen.CaptureText(Id);
    public TerminalTextSlice CaptureBoundedText(int maximumLines, int maximumCharacters, TerminalTextRange? selection = null)
        => _screen.CaptureBoundedText(maximumLines, maximumCharacters, selection);

    public async Task WriteInputAsync(ReadOnlyMemory<char> input, CancellationToken cancellationToken)
    {
        if (input.IsEmpty)
        {
            return;
        }

        var encoder = new System.Text.UTF8Encoding(false, false).GetEncoder();
        var offset = 0;
        while (offset < input.Length)
        {
            var bytes = new byte[_options.InputChunkSize];
            encoder.Convert(
                input.Span.Slice(offset),
                bytes,
                flush: false,
                out var charactersUsed,
                out var bytesUsed,
                out _);
            if (charactersUsed == 0)
            {
                throw new InvalidOperationException("无法增量编码终端输入。");
            }
            offset += charactersUsed;
            if (bytesUsed > 0)
            {
                await WriteBytesAsync(bytes.AsMemory(0, bytesUsed), cancellationToken).ConfigureAwait(false);
            }
        }

        if (offset == input.Length)
        {
            var trailing = new byte[8];
            encoder.Convert(ReadOnlySpan<char>.Empty, trailing, true, out _, out var bytesUsed, out _);
            if (bytesUsed > 0)
            {
                await WriteBytesAsync(trailing.AsMemory(0, bytesUsed), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task SubmitInputAsync(ReadOnlyMemory<char> input, CancellationToken cancellationToken) =>
        WriteInputAsync(input, cancellationToken);

    internal async Task WriteBytesAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken)
    {
        lock (_lifecycleSync)
        {
            if (!_acceptingInput || _state != SessionState.Running)
            {
                throw new InvalidOperationException("会话未处于可输入状态。");
            }
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _writeCancellation.Token);
        await _input.Writer.WriteAsync(input.ToArray(), linked.Token).ConfigureAwait(false);
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        if (columns <= 0 || rows <= 0 || columns > short.MaxValue || rows > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "终端尺寸必须在有效范围内。");
        }

        ResizeRequest request;
        lock (_resizeSync)
        {
            if (!_acceptingInput)
            {
                return Task.CompletedTask;
            }
            _pendingResize?.Completion.TrySetResult();
            request = new ResizeRequest(columns, rows);
            _pendingResize = request;
        }
        try
        {
            if (_resizeSignal.CurrentCount == 0)
            {
                _resizeSignal.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            return Task.CompletedTask;
        }
        return request.Completion.Task.WaitAsync(cancellationToken);
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Task closeTask;
        lock (_lifecycleSync)
        {
            closeTask = _closeTask ??= Task.Run(CloseCoreAsync);
        }
        return closeTask.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        lock (_lifecycleSync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        TransitionTo(SessionState.Disposed);
        _readThread?.Dispose();
        _writeThread?.Dispose();
        _writeCancellation.Dispose();
        _resizeCancellation.Dispose();
        _resizeSignal.Dispose();
    }

    private async Task CloseCoreAsync()
    {
        try
        {
            TransitionTo(SessionState.Exiting);
            lock (_lifecycleSync)
            {
                _acceptingInput = false;
            }
            _input.Writer.TryComplete();
            _writeCancellation.Cancel();
            _resizeCancellation.Cancel();
            lock (_resizeSync)
            {
                _pendingResize?.Completion.TrySetCanceled();
                _pendingResize = null;
            }
            CancelSynchronousIo(_writeThread);

            var process = _process;
            if (_remote is not null)
            {
                await _remote.DisposeAsync().ConfigureAwait(false);
                await WaitForWorkerAsync(_writeTask).ConfigureAwait(false);
                await WaitForWorkerAsync(_resizeTask).ConfigureAwait(false);
                return;
            }
            if (process is null)
            {
                return;
            }
            process.CloseInput();
            await Task.Run(() => process.Wait(ToMilliseconds(_options.ProcessExitGrace))).ConfigureAwait(false);
            if (!process.HasExited)
            {
                process.TerminateTree();
                await Task.Run(() => process.Wait(ToMilliseconds(_options.ProcessExitGrace))).ConfigureAwait(false);
            }

            var closePseudoConsole = Task.Run(process.ClosePseudoConsole);
            if (_readTask is not null)
            {
                try
                {
                    await _readTask.WaitAsync(_options.OutputDrainGrace).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
            if (_readTask is null || !_readTask.IsCompleted)
            {
                CancelSynchronousIo(_readThread);
                process.CloseOutput();
            }
            try
            {
                await closePseudoConsole.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
            await WaitForWorkerAsync(_writeTask).ConfigureAwait(false);
            await WaitForWorkerAsync(_resizeTask).ConfigureAwait(false);
            await WaitForWorkerAsync(_processExitTask).ConfigureAwait(false);
            await Task.Run(process.Dispose).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            if (State is not SessionState.Failed)
            {
                TransitionTo(SessionState.Exited);
            }
        }
    }

    private static async Task WaitForWorkerAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task WatchProcessExitAsync(ConPtyProcess process)
    {
        try
        {
            await process.WaitForExitAsync(_resizeCancellation.Token).ConfigureAwait(false);
            _ = CloseAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ReadLoop()
    {
        _diagnostics.SetReadWorker(true);
        try
        {
            _readThread = NativeMethods.OpenThread(NativeMethods.ThreadTerminate, false, NativeMethods.GetCurrentThreadId());
            var process = _process ?? throw new InvalidOperationException("会话进程尚未创建。");
            using var stream = new FileStream(process.OutputRead, FileAccess.Read, 1, isAsync: false);
            var bytes = new byte[_options.ReadBufferSize];
            while (true)
            {
                var count = stream.Read(bytes, 0, bytes.Length);
                if (count == 0)
                {
                    break;
                }
                _diagnostics.AddBytesRead(count);
                _streamDecoder.Write(bytes, count);
                PublishPlainText();
                PublishOutput(bytes.AsMemory(0, count));
                PublishScreenChanges();
            }

            _streamDecoder.Complete();
            PublishPlainText();
            PublishScreenChanges();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            if (State != SessionState.Exiting)
            {
                TransitionTo(SessionState.Failed, exception.Message);
            }
        }
        catch (Exception exception)
        {
            TransitionTo(SessionState.Failed, exception.Message);
        }
        finally
        {
            _diagnostics.SetReadWorker(false);
            if (State != SessionState.Exiting)
            {
                _ = Task.Run(() => CloseAsync(CancellationToken.None));
            }
        }
    }

    private void WriteLoop()
    {
        _diagnostics.SetWriteWorker(true);
        try
        {
            _writeThread = NativeMethods.OpenThread(NativeMethods.ThreadTerminate, false, NativeMethods.GetCurrentThreadId());
            var process = _process ?? throw new InvalidOperationException("会话进程尚未创建。");
            using var stream = new FileStream(process.InputWrite, FileAccess.Write, 1, isAsync: false);
            while (!_writeCancellation.IsCancellationRequested)
            {
                if (!_input.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    break;
                }
                while (_input.Reader.TryRead(out var bytes))
                {
                    stream.Write(bytes.Span);
                    stream.Flush();
                    _diagnostics.AddInputBytes(bytes.Length);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            if (State == SessionState.Running)
            {
                TransitionTo(SessionState.Failed, exception.Message);
            }
        }
        catch (Exception exception)
        {
            TransitionTo(SessionState.Failed, exception.Message);
        }
        finally
        {
            _diagnostics.SetWriteWorker(false);
        }
    }

    private void DecodeOutput(byte[] bytes)
    {
        _diagnostics.AddBytesRead(bytes.Length);
        _streamDecoder.Write(bytes, bytes.Length);
        PublishPlainText(); PublishOutput(bytes); PublishScreenChanges();
    }
    private void PublishPlainText()
    {
        if (_plainText.Length == 0) return;
        var text = _plainText.ToString(); _plainText.Clear(); TextReceived?.Invoke(text);
    }
    private async Task RemoteWriteLoopAsync()
    {
        _diagnostics.SetWriteWorker(true);
        try
        {
            await foreach (var bytes in _input.Reader.ReadAllAsync(_writeCancellation.Token).ConfigureAwait(false))
                for (var offset = 0; offset < bytes.Length; offset += ElevationProtocol.MaxIoBytes)
                {
                    var chunk = bytes.Slice(offset, Math.Min(ElevationProtocol.MaxIoBytes, bytes.Length - offset));
                    await _remote!.SendAsync(ElevationMessage.Input, chunk, _writeCancellation.Token).ConfigureAwait(false);
                    _diagnostics.AddInputBytes(chunk.Length);
                }
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        { if (State == SessionState.Running) { TransitionTo(SessionState.Failed, "管理员输入连接已断开。"); _ = Task.Run(() => CloseAsync(CancellationToken.None)); } }
        finally { _diagnostics.SetWriteWorker(false); }
    }

    private async Task ResizeLoopAsync()
    {
        try
        {
            while (true)
            {
                await _resizeSignal.WaitAsync(_resizeCancellation.Token).ConfigureAwait(false);
                ResizeRequest? request;
                lock (_resizeSync)
                {
                    request = _pendingResize;
                    _pendingResize = null;
                }
                if (request is null || !_acceptingInput)
                {
                    continue;
                }
                try
                {
                    _process?.Resize(request.Columns, request.Rows);
                    if (_remote is not null) await _remote.ResizeAsync(request.Columns, request.Rows).ConfigureAwait(false);
                    _screen.Resize(request.Columns, request.Rows);
                    PublishScreenChanges();
                    request.Completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    request.Completion.TrySetException(exception);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void WriteResponse(ReadOnlyMemory<byte> response)
    {
        if (State == SessionState.Running)
        {
            _ = WriteBytesAsync(response, CancellationToken.None).ContinueWith(
                task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void PublishOutput(ReadOnlyMemory<byte> bytes)
    {
        if (OutputReceived is not null)
        {
            OutputReceived.Invoke(this, new SessionOutputEventArgs { SessionId = Id, Bytes = bytes.ToArray() });
        }
    }

    private void PublishScreenChanges()
    {
        if (!_screen.HasChanges || SnapshotChanged is null || Interlocked.CompareExchange(ref _notificationPending, 1, 0) != 0)
        {
            return;
        }
        _ = Task.Run(() =>
        {
            while (true)
            {
                var changed = _screen.FlushChanges(Id);
                if (changed is not null)
                {
                    SnapshotChanged?.Invoke(this, changed);
                }
                Interlocked.Exchange(ref _notificationPending, 0);
                if (!_screen.HasChanges || SnapshotChanged is null ||
                    Interlocked.CompareExchange(ref _notificationPending, 1, 0) != 0)
                {
                    return;
                }
            }
        });
    }

    private void TransitionTo(SessionState state, string? error = null)
    {
        lock (_lifecycleSync)
        {
            if (_state == SessionState.Disposed || _state == state)
            {
                return;
            }
            if (_state == SessionState.Failed && state == SessionState.Exiting)
            {
                return;
            }
            _state = state;
        }
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs { SessionId = Id, State = state, Error = error });
    }

    private static void CancelSynchronousIo(SafeThreadHandle? thread)
    {
        if (thread is not null && !thread.IsInvalid && !thread.IsClosed)
        {
            NativeMethods.CancelSynchronousIo(thread);
        }
    }

    private static uint ToMilliseconds(TimeSpan timeout) => (uint)Math.Clamp(timeout.TotalMilliseconds, 1, int.MaxValue);

    private sealed class ResizeRequest(int columns, int rows)
    {
        internal int Columns { get; } = columns;
        internal int Rows { get; } = rows;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
