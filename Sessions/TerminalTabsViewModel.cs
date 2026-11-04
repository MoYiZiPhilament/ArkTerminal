using System.Collections.ObjectModel;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Recovery;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Sessions;

public sealed class TerminalTabsViewModel
{
    private readonly ISessionManager _sessions;
    private readonly SynchronizationContext? _ui;
    private TabViewModel _activeTab;
    private bool _closing;

    public TerminalTabsViewModel(ISessionManager sessions)
    {
        _sessions = sessions;
        _ui = SynchronizationContext.Current;
        _activeTab = new TabViewModel(isNewPage: true);
        Tabs.Add(_activeTab);
        _activeTab.IsActive = true;
    }

    public event Action? ActiveTabChanged;
    public event Action? LayoutChanged;
    public event Action<TabViewModel>? TabDetaching;
    public Func<SessionLaunchRequest, Task<SessionLaunchRequest?>>? ConfirmElevatedLaunch { get; set; }
    public Func<TabViewModel, SessionLaunchRequest, Task<SessionLaunchRequest?>>? PrepareAgentLaunch { get; set; }
    public event Action<ConPtySession>? SessionAttached;
    public ObservableCollection<TabViewModel> Tabs { get; } = [];
    public TabViewModel ActiveTab { get => _activeTab; private set { if (_activeTab == value) return; _activeTab.IsActive = false; _activeTab = value; _activeTab.IsActive = true; ActiveTabChanged?.Invoke(); LayoutChanged?.Invoke(); } }
    public IEnumerable<TabViewModel> RunningTabs => Tabs.Where(tab => tab.Session?.State == SessionState.Running);

    public void Activate(TabViewModel? tab)
    {
        if (tab is not null && Tabs.Contains(tab) && !_closing)
        {
            ActiveTab = tab;
        }
    }

    public TabViewModel ShowNewPage()
    {
        var page = Tabs.FirstOrDefault(tab => tab.IsNewPage);
        if (page is null)
        {
            page = new TabViewModel(isNewPage: true);
            Tabs.Insert(0, page);
            LayoutChanged?.Invoke();
        }
        ActiveTab = page;
        return page;
    }

    public TabViewModel AddPlaceholder(TabLaunchConfiguration launch, string? fixedTitle = null, bool recovered = false)
    {
        if (Tabs.Count >= 100)
        {
            throw new InvalidOperationException("标签数量已达到 100 个上限。");
        }
        var tab = new TabViewModel(launch) { FixedTitle = fixedTitle, IsRecoveredPlaceholder = recovered };
        Tabs.Add(tab);
        ActiveTab = tab;
        LayoutChanged?.Invoke();
        return tab;
    }

    public async Task<TabViewModel> LaunchAsync(TabLaunchConfiguration launch, string? fixedTitle = null, CancellationToken cancellationToken = default)
    {
        var tab = AddPlaceholder(launch, fixedTitle);
        await StartAsync(tab, cancellationToken);
        return tab;
    }

    public async Task StartAsync(TabViewModel tab, CancellationToken cancellationToken = default)
    {
        if (tab.Launch is null || tab.State == TabRuntimeState.Running || _closing)
        {
            return;
        }
        await tab.OperationGate.WaitAsync(cancellationToken);
        try
        {
            await StartCoreAsync(tab, cancellationToken);
        }
        finally
        {
            tab.OperationGate.Release();
        }
    }

    public async Task<TabViewModel?> DuplicateAsync(TabViewModel tab, CancellationToken cancellationToken = default)
    {
        if (tab.Launch is null || _closing)
        {
            return null;
        }
        var duplicate = AddPlaceholder(tab.Launch.Clone(), tab.FixedTitle);
        await StartAsync(duplicate, cancellationToken);
        return duplicate;
    }

    public async Task RestartAsync(TabViewModel tab, CancellationToken cancellationToken = default)
        => await RestartAsync(tab, tab.Launch, cancellationToken);

    public async Task RestartAsync(TabViewModel tab, TabLaunchConfiguration? replacement, CancellationToken cancellationToken = default)
    {
        if (tab.Launch is null || _closing)
        {
            return;
        }
        await tab.OperationGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(tab, cancellationToken);
            tab.Launch = replacement;
            tab.ZoomFontSize = null;
            tab.ScrollbackOffset = 0;
            tab.SearchQuery = string.Empty;
            await StartCoreAsync(tab, cancellationToken);
        }
        finally
        {
            tab.OperationGate.Release();
        }
    }

    public async Task CloseAsync(TabViewModel tab, CancellationToken cancellationToken = default)
    {
        if (tab.IsNewPage || _closing)
        {
            return;
        }
        await tab.OperationGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(tab, cancellationToken);
            var index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            if (ActiveTab == tab)
            {
                ActiveTab = Tabs[Math.Clamp(index - 1, 0, Tabs.Count - 1)];
            }
            LayoutChanged?.Invoke();
        }
        finally
        {
            tab.OperationGate.Release();
        }
    }

    public void Move(TabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        if (oldIndex < 0 || tab.IsNewPage)
        {
            return;
        }
        newIndex = Math.Clamp(newIndex, Tabs.Any(item => item.IsNewPage) ? 1 : 0, Tabs.Count - 1);
        if (oldIndex != newIndex)
        {
            Tabs.Move(oldIndex, newIndex);
            LayoutChanged?.Invoke();
        }
    }

    public void Rename(TabViewModel tab, string? title)
    {
        tab.FixedTitle = title;
        LayoutChanged?.Invoke();
    }

    public void Next(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }
        var index = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index + delta % Tabs.Count + Tabs.Count) % Tabs.Count];
    }

    public IReadOnlyList<RecoveryTabState> CaptureLayout() => Tabs.Where(tab => tab.Launch is { IsTransientAgent: false }).Select((tab, index) => tab.ToRecoveryState(index)).ToArray();

    public void RemoveRecoveredPlaceholders()
    {
        foreach (var tab in Tabs.Where(tab => tab.IsRecoveredPlaceholder && tab.Session is null).ToArray())
        {
            Tabs.Remove(tab);
        }
        if (!Tabs.Contains(ActiveTab))
        {
            ShowNewPage();
        }
        LayoutChanged?.Invoke();
    }

    public async Task CloseAllAsync()
    {
        _closing = true;
        TabDetaching?.Invoke(ActiveTab);
        foreach (var tab in Tabs.Where(tab => tab.Session is not null).ToArray())
        {
            await tab.OperationGate.WaitAsync();
            try
            {
                await StopCoreAsync(tab, CancellationToken.None);
            }
            finally
            {
                tab.OperationGate.Release();
            }
        }
    }

    private async Task StartCoreAsync(TabViewModel tab, CancellationToken cancellationToken)
    {
        if (tab.Session is not null || tab.Launch is null)
        {
            return;
        }
        tab.Error = null;
        tab.State = TabRuntimeState.Starting;
        try
        {
            var request = tab.Launch.Request;
            if (request.AgentRecipe is not null)
            {
                request = PrepareAgentLaunch is null ? throw new InvalidOperationException("Agent 启动入口尚未就绪。") : await PrepareAgentLaunch(tab, request);
                if (request is null) { tab.State = TabRuntimeState.NotStarted; return; }
            }
            if (request.RunAsAdministrator)
            {
                var approved = ConfirmElevatedLaunch is null ? null : await ConfirmElevatedLaunch(request);
                if (approved is null) { tab.State = TabRuntimeState.NotStarted; return; }
                request = approved;
            }
            var session = (ConPtySession)await _sessions.CreateAsync(request, cancellationToken);
            tab.Session = session;
            SessionAttached?.Invoke(session);
            tab.SessionGeneration++;
            var generation = tab.SessionGeneration;
            session.StateChanged += (_, args) => Post(() => OnSessionState(tab, session, generation, args));
            var lastTitle = session.ShellTitle;
            tab.ShellTitle = lastTitle;
            session.SnapshotChanged += (_, args) =>
            {
                var title = session.ShellTitle;
                if (lastTitle == title) return;
                lastTitle = title;
                Post(() => OnSnapshot(tab, session, generation, args.SessionId, title));
            };
            tab.State = session.State == SessionState.Running ? TabRuntimeState.Running : TabRuntimeState.Exited;
            if (ActiveTab == tab)
            {
                ActiveTabChanged?.Invoke();
            }
        }
        catch (OperationCanceledException exception)
        {
            tab.State = TabRuntimeState.NotStarted;
            tab.Error = exception.Message;
            if (ActiveTab == tab) ActiveTabChanged?.Invoke();
        }
        catch (Exception exception)
        {
            tab.State = TabRuntimeState.Failed;
            tab.Error = exception.Message;
            if (ActiveTab == tab)
            {
                ActiveTabChanged?.Invoke();
            }
        }
    }

    private async Task StopCoreAsync(TabViewModel tab, CancellationToken cancellationToken)
    {
        var session = tab.Session;
        if (session is null)
        {
            return;
        }
        TabDetaching?.Invoke(tab);
        tab.SessionGeneration++;
        tab.State = TabRuntimeState.Closing;
        await _sessions.CloseAsync(session.Id, cancellationToken);
        await session.DisposeAsync();
        tab.Session = null;
        tab.ShellTitle = string.Empty;
        tab.State = TabRuntimeState.NotStarted;
    }

    private void OnSessionState(TabViewModel tab, ConPtySession session, long generation, SessionStateChangedEventArgs args)
    {
        if (!Tabs.Contains(tab) || tab.Session != session || tab.SessionGeneration != generation || args.SessionId != session.Id)
        {
            return;
        }
        tab.Error = args.Error;
        tab.State = args.State switch
        {
            SessionState.Starting => TabRuntimeState.Starting,
            SessionState.Running => TabRuntimeState.Running,
            SessionState.Failed => TabRuntimeState.Failed,
            SessionState.Exited or SessionState.Disposed => TabRuntimeState.Exited,
            _ => tab.State
        };
        if (ActiveTab == tab)
        {
            ActiveTabChanged?.Invoke();
        }
    }

    private void OnSnapshot(TabViewModel tab, ConPtySession session, long generation, Guid sessionId, string title)
    {
        if (tab.Session != session || tab.SessionGeneration != generation || sessionId != session.Id)
        {
            return;
        }
        tab.ShellTitle = title;
    }

    private void Post(Action action)
    {
        if (_ui is null)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }
}
