using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArkTerminal.Core.Mcp;

[Flags]
public enum McpReadFields { None = 0, Profiles = 1, Sessions = 2, Metadata = 4, WorkingDirectory = 8, Output = 16, Selection = 32 }

public static class McpLimits
{
    public const int Version = 1;
    public const int MaxConnections = 4;
    public const int MaxMessageBytes = 128 * 1024;
    public const int MaxPipeBytes = 256 * 1024;
    // A 64 KiB text payload may be escaped twice inside MCP text content.
    public const int MaxResponseMessageBytes = 1024 * 1024;
    public const int MaxRequestBytes = 4096;
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static McpReadFields RequiredField(string tool) => tool switch
    {
        "list_profiles" => McpReadFields.Profiles,
        "list_sessions" => McpReadFields.Sessions,
        "get_session_metadata" => McpReadFields.Metadata,
        "get_output_tail" => McpReadFields.Output,
        "get_selection" => McpReadFields.Selection,
        _ => McpReadFields.None
    };
}

public sealed record McpPipeHello(int Version, Guid InstanceId);
public sealed record McpReadRequest(string Tool, Guid? SessionId = null, int MaxLines = Context.ContextLimits.MaxLines,
    int MaxBytes = Context.ContextLimits.MaxBytes, string? ClientLabel = null);
public sealed record McpReadResponse(string Status, JsonElement? Data = null)
{
    public static McpReadResponse Result(object data) => new("ok", JsonSerializer.SerializeToElement(data, McpLimits.Json));
}

public sealed record McpReadGrant(Guid SessionId, McpReadFields Fields);
public sealed record McpConnectionInfo(Guid Id, int ProcessId, string ClientLabel, string Status, McpReadGrant? Grant, long AllowedReads, long DeniedReads);
