namespace ArkTerminal.Core.Agents;

public enum AgentDetectionStatus
{
    Verified,
    FoundUnverified,
    NotInstalled,
    TimedOut,
    InvalidConfiguration,
    Unsupported
}

public sealed class AgentDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ExecutableCandidates { get; set; } = [];
    public string? OverridePath { get; set; }
    public AgentVersionProbe? VersionProbe { get; set; }
    public List<string> LaunchTemplate { get; set; } = [];
    public List<string> Variables { get; set; } = [];
    public AgentFields Fields { get; set; } = new();
    public AgentCapabilities Capabilities { get; set; } = new();
    public List<string> StatusSignals { get; set; } = ["process"];
    public string Evidence { get; set; } = string.Empty;
    public string? UnsupportedReason { get; set; }
}

public sealed class AgentVersionProbe
{
    public List<string> Arguments { get; set; } = ["--version"];
    public int TimeoutMilliseconds { get; set; } = 3000;
    public int MaxOutputBytes { get; set; } = 65536;
}

public sealed class AgentFields
{
    public List<string> Model { get; set; } = [];
    public Dictionary<string, List<string>> PermissionModes { get; set; } = new(StringComparer.Ordinal);
    public List<AgentArgumentOption> Arguments { get; set; } = [];
}

public sealed class AgentArgumentOption
{
    public string Flag { get; set; } = string.Empty;
    public bool TakesValue { get; set; }
    public bool Repeatable { get; set; }
    public List<string> AllowedValues { get; set; } = [];
}

public sealed class AgentCapabilities
{
    public bool InteractiveInput { get; set; }
    public bool Resume { get; set; }
    // Text input uses the terminal. A machine protocol is a separate, unimplemented contract.
    public bool ProgrammaticHandoff { get; set; }
}

public sealed class AgentDetectionOptions
{
    public int MaxConcurrency { get; init; } = 2;
    public int ProbeTimeoutMilliseconds { get; init; } = 3000;
    public int MaxProbeOutputBytes { get; init; } = 65536;
    public int MaxPathDirectories { get; init; } = 128;
    public int MaxAdapters { get; init; } = 32;
    public int MaxAdapterBytes { get; init; } = 65536;
    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromDays(7);
}

public static class AgentLimits
{
    public const int MaxAdapterBytes = 65536;
    public const int MaxJsonDepth = 8;
    public const int MaxAdapters = 32;
    public const int MaxRecipeArguments = 64;
    public const int MaxArgumentCharacters = 2048;
    public const int MaxExpandedCharacters = 8192;
}

public sealed class AgentDetectionResult
{
    public string DefinitionId { get; set; } = string.Empty;
    public AgentDetectionStatus Status { get; set; }
    public string? ResolvedPath { get; set; }
    public string? Version { get; set; }
    public string? Source { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; }
    public DateTimeOffset? ProbedAtUtc { get; set; }
    public long? FileLength { get; set; }
    public DateTime? LastWriteTimeUtc { get; set; }
    public string DefinitionRevision { get; set; } = string.Empty;
}

public interface IAgentDetector
{
    Task<IReadOnlyList<AgentDetectionResult>> DetectAsync(CancellationToken cancellationToken);
}

public sealed class ContextCollectionRequest
{
    public Guid SessionId { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public bool PreferSelection { get; init; } = true;
    public bool SelectionOnly { get; init; }
    public int MaxLines { get; init; } = Context.ContextLimits.MaxLines;
    public int MaxBytes { get; init; } = Context.ContextLimits.MaxBytes;
}

public sealed class ContextSnapshot
{
    public Guid SessionId { get; init; }
    public string Content { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool Truncated { get; init; }
    public int Redactions { get; init; }
    public bool HasSelection { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
}

public interface IContextCollector
{
    Task<ContextSnapshot> CollectAsync(ContextCollectionRequest request, CancellationToken cancellationToken);
}
