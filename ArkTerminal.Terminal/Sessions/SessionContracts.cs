using ArkTerminal.Core.Profiles;

namespace ArkTerminal.Terminal.Sessions;

public enum SessionState
{
    Created,
    Starting,
    Running,
    Exiting,
    Exited,
    Failed,
    Disposed
}

public sealed class SessionLaunchRequest
{
    public required string ProfileId { get; init; }
    public string? WorkspaceId { get; init; }
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string PostLaunchInput { get; init; } = string.Empty;
    public required string WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public int ScrollbackLines { get; init; } = 5000;
    public bool RunAsAdministrator { get; init; }
    public ShellIntegrationMode ShellIntegration { get; init; }
    public AgentLaunchRecipe? AgentRecipe { get; init; }

    public static SessionLaunchRequest FromProfile(TerminalProfile profile, string? workspaceId = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return new SessionLaunchRequest
        {
            ProfileId = profile.Id,
            WorkspaceId = workspaceId,
            ExecutablePath = profile.ExecutablePath,
            Arguments = profile.Arguments.ToArray(),
            PostLaunchInput = profile.PostLaunchInput,
            WorkingDirectory = profile.WorkingDirectory.Replace("{{home}}", home, StringComparison.OrdinalIgnoreCase),
            Environment = new Dictionary<string, string>(profile.Environment, StringComparer.OrdinalIgnoreCase),
            ScrollbackLines = profile.Terminal.ScrollbackLines,
            RunAsAdministrator = profile.RunAsAdministrator,
            ShellIntegration = profile.ShellIntegration,
            AgentRecipe = profile.AgentRecipe
        };
    }
}

public sealed class SessionOutputEventArgs : EventArgs
{
    public required Guid SessionId { get; init; }
    public required ReadOnlyMemory<byte> Bytes { get; init; }
}

public sealed class SessionStateChangedEventArgs : EventArgs
{
    public required Guid SessionId { get; init; }
    public required SessionState State { get; init; }
    public string? Error { get; init; }
}

public interface ISession : IAsyncDisposable
{
    Guid Id { get; }
    SessionState State { get; }
    event EventHandler<SessionOutputEventArgs>? OutputReceived;
    event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    Task WriteInputAsync(ReadOnlyMemory<char> input, CancellationToken cancellationToken);
    Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}

public interface ISessionManager : IAsyncDisposable
{
    IReadOnlyCollection<ISession> Sessions { get; }
    Task<ISession> CreateAsync(SessionLaunchRequest request, CancellationToken cancellationToken);
    ISession? GetSession(Guid sessionId);
    Task CloseAsync(Guid sessionId, CancellationToken cancellationToken);
}
