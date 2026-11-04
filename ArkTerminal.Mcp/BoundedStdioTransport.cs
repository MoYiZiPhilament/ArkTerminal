using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ArkTerminal.Core.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ArkTerminal.Mcp;

// Only framing and resource limits live here. The official SDK owns all MCP protocol semantics.
internal sealed class BoundedStdioTransport : ITransport
{
    private readonly Stream _input = Console.OpenStandardInput();
    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly Channel<JsonRpcMessage> _messages = Channel.CreateBounded<JsonRpcMessage>(8);
    private readonly ConcurrentDictionary<RequestId, byte> _requests = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    private int _disposed;
    public string? SessionId => null;
    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

    internal BoundedStdioTransport() => _reader = Task.Run(ReadAsync);

    private async Task ReadAsync()
    {
        var pending = new byte[McpLimits.MaxMessageBytes];
        var chunk = new byte[4096];
        var length = 0;
        try
        {
            while (true)
            {
                var count = await _input.ReadAsync(chunk, _stop.Token).ConfigureAwait(false);
                if (count == 0) break;
                for (var i = 0; i < count; i++)
                {
                    if (chunk[i] != (byte)'\n')
                    {
                        if (length == pending.Length) throw new IOException("message_limit");
                        pending[length++] = chunk[i];
                        continue;
                    }
                    if (length == 0) continue;
                    try
                    {
                        // SDK converters preserve JSON-RPC IDs, notifications, errors and version metadata.
                        var message = JsonSerializer.Deserialize<JsonRpcMessage>(pending.AsSpan(0, length), McpJsonUtilities.DefaultOptions);
                        if (message is JsonRpcRequest request)
                        {
                            if (_requests.Count >= 4 || !_requests.TryAdd(request.Id, 0))
                            {
                                await WriteAsync(new JsonRpcError { Id = request.Id, Error = new() { Code = -32600, Message = "Concurrent request limit or duplicate id." } }, _stop.Token).ConfigureAwait(false);
                                continue;
                            }
                        }
                        if (message is not null) await _messages.Writer.WriteAsync(message, _stop.Token).ConfigureAwait(false);
                    }
                    catch (JsonException)
                    {
                        await WriteAsync(new JsonRpcError { Id = default, Error = new() { Code = -32700, Message = "Parse error." } }, _stop.Token).ConfigureAwait(false);
                    }
                    finally { length = 0; }
                }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        { if (!_stop.IsCancellationRequested) Console.Error.WriteLine("ArkTerminal MCP: input closed or exceeded protocol limits."); }
        finally { _messages.Writer.TryComplete(); }
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        try { await WriteAsync(message, cancellationToken).ConfigureAwait(false); }
        finally { if (message is JsonRpcResponse or JsonRpcError && message is JsonRpcMessageWithId response) _requests.TryRemove(response.Id, out _); }
    }

    private async Task WriteAsync(JsonRpcMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonUtilities.DefaultOptions);
        if (bytes.Length > McpLimits.MaxResponseMessageBytes) throw new IOException("response_limit");
        await _send.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes, token).ConfigureAwait(false);
            await _output.WriteAsync("\n"u8.ToArray(), token).ConfigureAwait(false);
            await _output.FlushAsync(token).ConfigureAwait(false);
        }
        finally { _send.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _input.Dispose();
        await _reader.ConfigureAwait(false);
        _output.Dispose(); _send.Dispose(); _stop.Dispose();
    }
}
