using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using ArkTerminal.Core.Mcp;
using ArkTerminal.Terminal.Sessions.Elevation;

namespace ArkTerminal.Terminal.Mcp;

public static class McpPipe
{
    public static async Task WriteAsync<T>(Stream pipe, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, McpLimits.Json);
        if (bytes.Length > McpLimits.MaxPipeBytes) throw new IOException("MCP frame limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, token).ConfigureAwait(false);
        await pipe.WriteAsync(bytes, token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream pipe, int maximumBytes, CancellationToken token)
    {
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        using var frameTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        frameTimeout.CancelAfter(McpLimits.RequestTimeout);
        await pipe.ReadExactlyAsync(header.AsMemory(1), frameTimeout.Token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 2 || size > maximumBytes) throw new IOException("MCP frame limit.");
        var bytes = new byte[size];
        await pipe.ReadExactlyAsync(bytes, frameTimeout.Token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, McpLimits.Json) ?? throw new IOException("Invalid MCP frame.");
    }
}

public sealed class McpBridgeClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly ElevationProtocol.VerifiedPeer _peer;
    private readonly SemaphoreSlim _request = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _monitor;
    public CancellationToken Disconnected => _lifetime.Token;

    private McpBridgeClient(NamedPipeClientStream pipe, ElevationProtocol.VerifiedPeer peer)
    {
        _pipe = pipe; _peer = peer;
        _monitor = MonitorAsync();
    }

    public static async Task<McpBridgeClient> ConnectAsync(string pipeName, Guid instanceId, int uiPid, long uiStart, string uiPath, CancellationToken token)
    {
        if (!pipeName.StartsWith("ArkTerminal.Mcp.", StringComparison.Ordinal) || pipeName.Length > 100 ||
            pipeName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.')) throw new ArgumentException("Invalid endpoint.");
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Identification);
        ElevationProtocol.VerifiedPeer? peer = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(McpLimits.HandshakeTimeout);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            peer = ElevationProtocol.VerifyPeer(pipe, false, uiPid, uiStart, Path.GetFullPath(uiPath), false);
            await McpPipe.WriteAsync(pipe, new McpPipeHello(McpLimits.Version, instanceId), timeout.Token).ConfigureAwait(false);
            var reply = await McpPipe.ReadAsync<McpReadResponse>(pipe, McpLimits.MaxRequestBytes, timeout.Token).ConfigureAwait(false);
            if (reply.Status != "connected") throw new IOException("Endpoint rejected handshake.");
            return new(pipe, peer);
        }
        catch { peer?.Dispose(); pipe.Dispose(); throw; }
    }

    public async Task<McpReadResponse> ReadAsync(McpReadRequest request, CancellationToken token)
    {
        if (!await _request.WaitAsync(0, token).ConfigureAwait(false)) return new("busy");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            timeout.CancelAfter(McpLimits.RequestTimeout);
            await McpPipe.WriteAsync(_pipe, request, timeout.Token).ConfigureAwait(false);
            return await McpPipe.ReadAsync<McpReadResponse>(_pipe, McpLimits.MaxPipeBytes, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
        {
            // A cancelled request invalidates this serial stream; no late response can become the next result.
            _lifetime.Cancel(); _pipe.Dispose();
            token.ThrowIfCancellationRequested();
            return new("disconnected");
        }
        finally { _request.Release(); }
    }

    private async Task MonitorAsync()
    {
        try { await _peer.WaitForExitAsync(_lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        _lifetime.Cancel(); _pipe.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); _pipe.Dispose();
        await _monitor.ConfigureAwait(false);
        _peer.Dispose(); _lifetime.Dispose(); _request.Dispose();
    }
}
