using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArkTerminal.Terminal.Sessions.Elevation;

internal sealed class ElevatedConnection : IAsyncDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _helper;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<byte[]> _output;
    private readonly Action<string?> _exited;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _heartbeat = Task.CompletedTask;
    internal Task Completion => _completion.Task;

    private ElevatedConnection(NamedPipeServerStream pipe, Process helper, Action<byte[]> output, Action<string?> exited)
    { _pipe = pipe; _helper = helper; _output = output; _exited = exited; }

    internal static async Task<ElevatedConnection> StartAsync(Guid session, SessionLaunchRequest request, TerminalSessionOptions options,
        Action<byte[]> output, Action<string?> exited, CancellationToken token)
    {
        if (ElevationSupport.UnavailableReason is { } unsupported) throw new PlatformNotSupportedException(unsupported);
        var helperPath = Path.GetFullPath(options.ElevationHostPath ?? Path.Combine(AppContext.BaseDirectory, "ArkTerminal.exe"));
        if (!File.Exists(helperPath)) throw new FileNotFoundException(Localize.T("Text0512"), helperPath);
        var data = JsonSerializer.SerializeToUtf8Bytes(new ElevationLaunch(ElevationProtocol.Version, session, options.InitialColumns, options.InitialRows, request));
        if (data.Length >= ElevationProtocol.MaxFrameBytes) throw new ArgumentException(Localize.T("Text0513"));
        var hash = Convert.ToHexString(SHA256.HashData(data));
        var name = "ArkTerminal.Elevation." + Guid.NewGuid().ToString("N");
        var pipe = ElevationProtocol.CreateLocalServer(name);
        Process? helper = null;
        try
        {
            using var current = Process.GetCurrentProcess();
            var info = new ProcessStartInfo(helperPath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(helperPath)! };
            foreach (var argument in new[] { "--elevated-host", name, current.Id.ToString(), current.StartTime.ToUniversalTime().Ticks.ToString(), hash }) info.ArgumentList.Add(argument);
            // ShellExecute/UAC runs off the dispatcher. No shell exists before the authenticated launch frame.
            helper = await Task.Run(() => Process.Start(info) ?? throw new IOException(Localize.T("Text0514"))).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ElevationProtocol.HandshakeTimeout);
            var helperStart = helper.StartTime.ToUniversalTime().Ticks;
            var connect = pipe.WaitForConnectionAsync(timeout.Token);
            var helperExit = helper.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connect, helperExit).ConfigureAwait(false) != connect) throw new IOException(Localize.T("Text0515"));
            await connect.ConfigureAwait(false);
            using var peer = ElevationProtocol.VerifyPeer(pipe, true, helper.Id, helperStart, helperPath, requireElevated: true);
            await ElevationProtocol.SendAsync(pipe, ElevationMessage.Launch, data, timeout.Token).ConfigureAwait(false);
            var ready = await ElevationProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (ready.Type != ElevationMessage.Ready || !ready.Payload.AsSpan().SequenceEqual(session.ToByteArray()))
                throw new IOException(ready.Type == ElevationMessage.Error ? Encoding.UTF8.GetString(ready.Payload) : Localize.T("Text0516"));
            var connection = new ElevatedConnection(pipe, helper, output, exited);
            return connection;
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        { pipe.Dispose(); helper?.Dispose(); throw new OperationCanceledException(Localize.T("Text0517"), error, token); }
        catch
        { pipe.Dispose(); helper?.Dispose(); throw; }
    }

    internal void BeginReading()
    {
        _heartbeat = Task.Run(HeartbeatAsync);
        _ = Task.Run(ReadAsync);
    }

    internal async Task SendAsync(ElevationMessage type, ReadOnlyMemory<byte> payload, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        linked.CancelAfter(ElevationProtocol.HeartbeatTimeout);
        await _send.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await ElevationProtocol.SendAsync(_pipe, type, payload, linked.Token).ConfigureAwait(false); }
        finally { _send.Release(); }
    }
    internal Task ResizeAsync(int columns, int rows)
    {
        var bytes = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(bytes, columns); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), rows);
        return SendAsync(ElevationMessage.Resize, bytes, CancellationToken.None);
    }
    private async Task ReadAsync()
    {
        string? error = null;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var frame = await ElevationProtocol.ReadAsync(_pipe, _lifetime.Token).ConfigureAwait(false);
                if (frame.Type == ElevationMessage.Output && frame.Payload.Length <= ElevationProtocol.MaxIoBytes) _output(frame.Payload);
                else if (frame.Type == ElevationMessage.Exited && frame.Payload.Length == 0) break;
                else if (frame.Type == ElevationMessage.Error) { error = Encoding.UTF8.GetString(frame.Payload.AsSpan(0, Math.Min(frame.Payload.Length, 2048))); break; }
                else throw new IOException(Localize.T("Text0518"));
            }
        }
        catch (Exception failure) when (failure is IOException or ObjectDisposedException or OperationCanceledException)
        { if (!_lifetime.IsCancellationRequested) error = Localize.T("Text0519"); }
        finally
        {
            _lifetime.Cancel(); _pipe.Dispose();
            try { _exited(error); }
            finally { _completion.TrySetResult(); }
        }
    }
    private async Task HeartbeatAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false)) await SendAsync(ElevationMessage.Ping, ReadOnlyMemory<byte>.Empty, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { _pipe.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (!_lifetime.IsCancellationRequested)
        {
            try { await SendAsync(ElevationMessage.Close, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
        }
        _lifetime.Cancel(); _pipe.Dispose();
        await Completion.ConfigureAwait(false); await _heartbeat.ConfigureAwait(false);
        try { await _helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException) { /* The elevated host independently watches this pipe and the UI kernel handle. */ }
        _helper.Dispose(); _send.Dispose(); _lifetime.Dispose();
    }
}
