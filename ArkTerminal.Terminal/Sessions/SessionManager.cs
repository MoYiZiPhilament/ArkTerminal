using ArkTerminal.Terminal.Sessions.Windows;

namespace ArkTerminal.Terminal.Sessions;

public sealed class SessionManager : ISessionManager
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ConPtySession> _sessions = [];
    private readonly TerminalSessionOptions _options;
    private bool _disposed;

    public SessionManager(TerminalSessionOptions? options = null)
    {
        _options = options ?? new TerminalSessionOptions();
        _options.Validate();
    }

    public IReadOnlyCollection<ISession> Sessions
    {
        get
        {
            lock (_sync)
            {
                return _sessions.Values.ToArray();
            }
        }
    }

    public event Action<ConPtySession>? SessionCreated;

    public async Task<ISession> CreateAsync(SessionLaunchRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ShellLaunchAdapter.Validate(request);
        var options = new TerminalSessionOptions
        {
            ElevationHostPath = _options.ElevationHostPath,
            InitialColumns = _options.InitialColumns,
            InitialRows = _options.InitialRows,
            ReadBufferSize = _options.ReadBufferSize,
            InputQueueCapacity = _options.InputQueueCapacity,
            InputChunkSize = _options.InputChunkSize,
            MaxCsiLength = _options.MaxCsiLength,
            MaxCsiParameters = _options.MaxCsiParameters,
            MaxOscLength = _options.MaxOscLength,
            MaxScrollbackLines = Math.Clamp(request.ScrollbackLines, 0, 100_000),
            ProcessExitGrace = _options.ProcessExitGrace,
            OutputDrainGrace = _options.OutputDrainGrace
        };
        var session = new ConPtySession(options);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sessions.Add(session.Id, session);
            session.StateChanged += Session_StateChanged;
        }

        try
        {
            SessionCreated?.Invoke(session);
            await session.StartAsync(request, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            lock (_sync)
            {
                _sessions.Remove(session.Id);
            }
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ISession? GetSession(Guid sessionId)
    {
        lock (_sync)
        {
            return _sessions.GetValueOrDefault(sessionId);
        }
    }

    public async Task CloseAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        ConPtySession? session;
        lock (_sync)
        {
            _sessions.TryGetValue(sessionId, out session);
        }
        if (session is null)
        {
            return;
        }

        await session.CloseAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _sessions.Remove(sessionId);
        }
    }

    private void Session_StateChanged(object? sender, SessionStateChangedEventArgs e)
    {
        if (e.State != SessionState.Disposed)
        {
            return;
        }
        lock (_sync)
        {
            _sessions.Remove(e.SessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        ConPtySession[] sessions;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            sessions = _sessions.Values.ToArray();
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }
}
