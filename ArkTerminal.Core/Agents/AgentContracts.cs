using ArkTerminal.Core.Profiles;

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
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ExecutableCandidates { get; set; } = [];
    public string? OverridePath { get; set; }
    public AgentLaunchRecipe LaunchRecipe { get; set; } = new();
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
}

public interface IAgentDetector
{
    Task<IReadOnlyList<AgentDetectionResult>> DetectAsync(CancellationToken cancellationToken);
}

public sealed class ContextCollectionRequest
{
    public Guid SessionId { get; init; }
    public string Purpose { get; init; } = string.Empty;
}

public sealed class ContextSnapshot
{
    public Guid SessionId { get; init; }
    public string Content { get; init; } = string.Empty;
}

public interface IContextCollector
{
    Task<ContextSnapshot> CollectAsync(ContextCollectionRequest request, CancellationToken cancellationToken);
}
