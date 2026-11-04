using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using ArkTerminal.Core.Context;
using ArkTerminal.Core.Mcp;
using ArkTerminal.Terminal.Sessions.Elevation;

namespace ArkTerminal.Terminal.Mcp;

public sealed class ReadOnlyMcpHost : IAsyncDisposable
{
    private sealed class Connection(int pid)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal int Pid { get; } = pid;
        internal string Label = "未声明（名称不可信）";
        internal string Status = "等待 UI 授权";
        internal McpReadGrant? Grant;
        internal int Revision;
        internal long Allowed, Denied;
    }

    private readonly object _sync = new();
    private readonly Dictionary<Guid, Connection> _connections = [];
    private readonly HashSet<Task> _workers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _bridgePath;
    private readonly Func<McpReadRequest, McpReadGrant, CancellationToken, Task<McpReadResponse>> _read;
    private NamedPipeServerStream? _listener;
    private Task? _accept;
    private int _disposed;
    public Guid InstanceId { get; } = Guid.NewGuid();
    public string PipeName { get; } = "ArkTerminal.Mcp." + Guid.NewGuid().ToString("N");
    public event Action? Changed;
    public event Action<bool>? ReadCompleted;
    public event Action? GrantChanged;
    public string Status { get; private set; } = "尚未启动";

    public ReadOnlyMcpHost(string bridgePath, Func<McpReadRequest, McpReadGrant, CancellationToken, Task<McpReadResponse>> read)
    { _bridgePath = Path.GetFullPath(bridgePath); _read = read; }

    public void Start()
    {
        if (_accept is not null) return;
        _accept = Task.Run(AcceptAsync);
    }

    public IReadOnlyList<McpConnectionInfo> Connections
    {
        get { lock (_sync) return _connections.Values.Select(c => new McpConnectionInfo(c.Id, c.Pid, c.Label, c.Status, c.Grant, c.Allowed, c.Denied)).ToArray(); }
    }

    public bool SetGrant(Guid connectionId, McpReadGrant? grant)
    {
        if (grant is not null && (grant.SessionId == Guid.Empty || (grant.Fields & ~(McpReadFields)63) != 0)) throw new ArgumentException("Invalid grant.");
        lock (_sync)
        {
            if (!_connections.TryGetValue(connectionId, out var connection)) return false;
            connection.Grant = grant; connection.Revision++;
            connection.Status = grant is null ? "已拒绝 / 撤销" : "已授权一个会话";
        }
        GrantChanged?.Invoke(); Changed?.Invoke(); return true;
    }

    private async Task AcceptAsync()
    {
        var first = true;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var pipe = ElevationProtocol.CreateLocalServer(PipeName, McpLimits.MaxConnections + 1, first);
                first = false;
                _listener = pipe;
                Status = "只读桥已就绪；每个连接需单独授权。";
                await pipe.WaitForConnectionAsync(_lifetime.Token).ConfigureAwait(false);
                _listener = null;
                lock (_sync)
                {
                    if (_workers.Count >= McpLimits.MaxConnections) { pipe.Dispose(); continue; }
                    var worker = Task.Run(() => ServeAsync(pipe));
                    _workers.Add(worker);
                    _ = worker.ContinueWith(done => { lock (_sync) _workers.Remove(done); }, TaskScheduler.Default);
                }
            }
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or ObjectDisposedException)
        { Status = _lifetime.IsCancellationRequested ? "已停止" : "只读桥不可用（本机管道创建失败）。"; }
        finally { _listener?.Dispose(); _listener = null; Changed?.Invoke(); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        Connection? connection = null;
        try
        {
            using (pipe)
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            {
                handshake.CancelAfter(McpLimits.HandshakeTimeout);
                var pid = ElevationProtocol.GetClientProcessId(pipe);
                using var process = Process.GetProcessById(pid);
                using var peer = ElevationProtocol.VerifyPeer(pipe, true, pid, process.StartTime.ToUniversalTime().Ticks, _bridgePath, false);
                var hello = await McpPipe.ReadAsync<McpPipeHello>(pipe, McpLimits.MaxRequestBytes, handshake.Token).ConfigureAwait(false);
                if (hello.Version != McpLimits.Version || hello.InstanceId != InstanceId) return;
                connection = new(pid);
                lock (_sync) _connections.Add(connection.Id, connection);
                await McpPipe.WriteAsync(pipe, new McpReadResponse("connected"), handshake.Token).ConfigureAwait(false);
                Changed?.Invoke();
                using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var monitor = MonitorClientAsync(peer, pipe, monitorStop.Token);
                try
                {
                    while (!_lifetime.IsCancellationRequested)
                    {
                        var request = await McpPipe.ReadAsync<McpReadRequest>(pipe, McpLimits.MaxRequestBytes, _lifetime.Token).ConfigureAwait(false);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        timeout.CancelAfter(McpLimits.RequestTimeout);
                        var (response, revision) = await ReadAuthorizedAsync(connection, request, timeout.Token).ConfigureAwait(false);
                        lock (_sync)
                        {
                            if (revision != connection.Revision) response = new("revoked");
                        }
                        await McpPipe.WriteAsync(pipe, response, timeout.Token).ConfigureAwait(false);
                        ReadCompleted?.Invoke(response.Status == "ok" || response.Status == "no_selection");
                        Changed?.Invoke();
                    }
                }
                finally { monitorStop.Cancel(); await monitor.ConfigureAwait(false); }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception or ArgumentException or InvalidOperationException or ObjectDisposedException) { }
        finally
        {
            pipe.Dispose();
            if (connection is not null) { lock (_sync) _connections.Remove(connection.Id); Changed?.Invoke(); }
        }
    }

    private async Task<(McpReadResponse Response, int Revision)> ReadAuthorizedAsync(Connection connection, McpReadRequest request, CancellationToken token)
    {
        McpReadGrant? grant;
        int revision;
        var field = McpLimits.RequiredField(request.Tool);
        lock (_sync)
        {
            connection.Label = ContextText.SafeField(request.ClientLabel ?? "未声明", 128);
            grant = connection.Grant; revision = connection.Revision;
            var denial = field == McpReadFields.None ? "unknown_tool" : grant is null ? "unauthorized" :
                !grant.Fields.HasFlag(field) ? "field_denied" :
                request.SessionId is { } id && id != grant.SessionId ? "session_denied" : null;
            if (denial is not null) { connection.Denied++; return (new(denial), revision); }
        }
        var bounded = request with { SessionId = grant!.SessionId, MaxLines = Math.Clamp(request.MaxLines, 1, ContextLimits.MaxLines), MaxBytes = Math.Clamp(request.MaxBytes, 1, ContextLimits.MaxBytes), ClientLabel = null };
        var result = await _read(bounded, grant, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_sync) { if (revision != connection.Revision) return (new("revoked"), connection.Revision); connection.Allowed++; }
        return (result, revision);
    }

    private static async Task MonitorClientAsync(ElevationProtocol.VerifiedPeer peer, NamedPipeServerStream pipe, CancellationToken token)
    {
        try { await peer.WaitForExitAsync(token).ConfigureAwait(false); pipe.Dispose(); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel(); _listener?.Dispose();
        if (_accept is not null) await _accept.ConfigureAwait(false);
        Task[] workers;
        lock (_sync) workers = _workers.ToArray();
        await Task.WhenAll(workers).ConfigureAwait(false);
        _lifetime.Dispose();
    }
}
