using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using ArkTerminal.Commands;
using ArkTerminal.Controls;
using ArkTerminal.Core.Context;
using ArkTerminal.Core.Diagnostics;
using ArkTerminal.Core.Mcp;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Sessions;
using ArkTerminal.Terminal.Mcp;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Sessions.Windows;

namespace ArkTerminal;

public partial class MainWindow
{
    private TerminalContextCollector _contexts = null!;
    private ContextSnapshotExporter _snapshots = null!;
    private ReadOnlyMcpHost _mcp = null!;
    private ContextWorkflowWindow? _contextWindow;
    private McpAccessWindow? _mcpWindow;
    private CommandReviewWindow? _commandReviewWindow;
    private bool _mcpRefreshPending;
    private readonly HashSet<Guid> _mcpNotified = [];

    private void InitializeStageNine()
    {
        _contexts = new(GetContextSourceAsync);
        _snapshots = new(_host.Paths);
        _mcp = new(Path.Combine(AppContext.BaseDirectory, "Mcp", "ArkTerminal.Mcp.exe"), ReadMcpAsync);
        _host.ReadOnlyMcp = _mcp;
        _mcp.ReadCompleted += allowed => _host.Diagnostics.Record(DiagnosticComponent.Context, allowed ? DiagnosticEventKind.McpReadAllowed : DiagnosticEventKind.McpReadDenied);
        _mcp.GrantChanged += () => _host.Diagnostics.Record(DiagnosticComponent.Context, DiagnosticEventKind.McpGrantChanged);
        _mcp.Changed += () =>
        {
            lock (_mcp)
            {
                if (_mcpRefreshPending || _allowClose || Dispatcher.HasShutdownStarted) return;
                _mcpRefreshPending = true;
            }
            Dispatcher.BeginInvoke(() =>
            {
                lock (_mcp) _mcpRefreshPending = false;
                var connections = _mcp.Connections;
                _mcpNotified.RemoveWhere(id => connections.All(c => c.Id != id));
                if (connections.Any(c => c.Status == Localize.T("Text0042") && _mcpNotified.Add(c.Id)))
                {
                    StatusText.Text = Localize.T("Text0043");
                    if (!_closing && _mcpWindow is null) ShowMcpAccess();
                }
            });
        };
        _mcp.Start();
        void Add(string id, string title, Func<Task> action) => _commands.Register(new CommandDescriptor
        { Id = id, Title = title, Category = Localize.T("Text0044"), Keywords = Localize.T("Text0045"),
            Shortcut = id == "ark-terminal-context" ? Localize.T("Text0046") : Localize.T("Text0047"), ExecuteAsync = _ => action() });
        Task Open(int purpose) { OpenContext(purpose); return Task.CompletedTask; }
        Add("ark-terminal-context", Localize.T("Text0048"), () => Open(0));
        Add("context.snapshot", Localize.T("Text0049"), () => Open(3));
        Add("context.explain", Localize.T("Text0050"), () => Open(1));
        Add("context.repair", Localize.T("Text0051"), () => Open(2));
        Add("context.review-command", Localize.T("Text0052"), () => { OpenCommandReview(); return Task.CompletedTask; });
        Add("mcp.manage", Localize.T("Text0053"), () => { ShowMcpAccess(); return Task.CompletedTask; });
        ShortcutList.ItemsSource = _commands.Search(string.Empty).Where(command => !command.Id.StartsWith("tab.activate.", StringComparison.Ordinal) && !command.Id.StartsWith("profile.launch.", StringComparison.Ordinal)).ToArray();
        _hotkey.ContextInvoked += () => Dispatcher.BeginInvoke(() => OpenContext(0));
        Closed += (_, _) => { _contextWindow?.Close(); _mcpWindow?.Close(); _commandReviewWindow?.Close(); };
    }

    private Task<TerminalContextSource?> GetContextSourceAsync(Guid id, CancellationToken token) => Dispatcher.InvokeAsync(() =>
    {
        var tab = _tabs.Tabs.FirstOrDefault(t => t.Session?.Id == id && t.State != TabRuntimeState.Closing);
        if (tab?.Session is not { } session || tab.Launch is null || _host.Sessions.GetSession(id) != session) return null;
        var shell = session.ShellStatus;
        var metadata = new ContextMetadata(id, tab.Launch.Request.ProfileId, tab.ProfileName, tab.Launch.Request.AgentRecipe is null ? "Shell" : "Agent",
            shell.WorkingDirectory ?? tab.WorkingDirectory, shell.WorkingDirectory is null ? Localize.T("Text0054") : Localize.T("Text0055"),
            session.VerifiedLaunchProcessName ?? Localize.T("Text0056"), session.State.ToString());
        return new TerminalContextSource(session, metadata, TerminalView.GetContextSelection(id));
    }, System.Windows.Threading.DispatcherPriority.Background, token).Task;

    private void OpenContext(int purpose)
    {
        if (_closing || _allowClose) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
        if (_contextWindow is not null) { _contextWindow.Activate(); return; }
        var tab = _tabs.ActiveTab;
        if (tab.Session is not { } session || tab.Launch is null) { StatusText.Text = Localize.T("Text0057"); return; }
        var targets = new List<ContextTarget> { new(Localize.T("Text0058")) };
        targets.AddRange(_tabs.Tabs.Where(t => t.Launch?.Request.AgentRecipe is not null).Select(t => new ContextTarget(
            Localize.F("Text0059", ContextText.SafeField(t.ProfileName), t.Session?.Id, ContextText.SafeField(t.WorkingDirectory, 4096)), t.Launch!.Clone(), t.Session?.Id)));
        targets.AddRange((_host.Catalog?.Profiles ?? []).Where(p => p.AgentRecipe is not null).Select(p => new ContextTarget(
            Localize.T("Text0060") + ContextText.SafeField(p.Name) + " · cwd：" + ContextText.SafeField(p.WorkingDirectory, 4096),
            TabLaunchConfiguration.FromProfile(p.DeepClone(), ExpandHome(p.WorkingDirectory), _host.FormalSettings?.Appearance))));
        _contextWindow = new(this, session.Id, _contexts, _snapshots, targets,
            () => !_closing && _tabs.Tabs.Any(t => t.Session == session && t.State != TabRuntimeState.Closing),
            async target =>
            {
                if (target.ExistingSessionId is { } targetId && !_tabs.Tabs.Any(t => t.Session?.Id == targetId)) throw new InvalidOperationException(Localize.T("Text0061"));
                var auxiliary = await _tabs.LaunchAsync(target.AgentLaunch!.Clone(), Localize.T("Text0062") + target.AgentLaunch.ProfileName);
                return auxiliary.Session?.State == SessionState.Running;
            }, RecordContextOperation, purpose);
        _contextWindow.Closed += (_, _) => _contextWindow = null;
        _contextWindow.Show();
    }

    private void RecordContextOperation(string operation) => _host.Diagnostics.Record(DiagnosticComponent.Context,
        operation switch { "copied" => DiagnosticEventKind.ContextCopied, "saved" => DiagnosticEventKind.SnapshotSaved,
            "cancelled" => DiagnosticEventKind.ContextCancelled, "failed" => DiagnosticEventKind.ContextFailed, _ => DiagnosticEventKind.ContextPrepared });

    private void OpenCommandReview()
    {
        if (_commandReviewWindow is not null) { _commandReviewWindow.Activate(); return; }
        var profiles = (_host.Catalog?.Profiles ?? []).Where(p => p.AgentRecipe is null &&
            Path.GetFileNameWithoutExtension(Environment.ExpandEnvironmentVariables(p.ExecutablePath)).ToLowerInvariant() is "pwsh" or "powershell").Select(p => p.DeepClone()).ToArray();
        _commandReviewWindow = new(this, profiles, _tabs.ActiveTab.Session?.ShellStatus.WorkingDirectory ?? _tabs.ActiveTab.WorkingDirectory, ExecuteReviewedCommandAsync, () => RecordContextOperation("copied"));
        _commandReviewWindow.Closed += (_, _) => _commandReviewWindow = null;
        _commandReviewWindow.Show();
    }

    private async Task<string> ExecuteReviewedCommandAsync(TerminalProfile profile, string cwd, string command)
    {
        ContextText.ValidatePreview(command);
        if (command.Length > ContextLimits.MaxCommandCharacters) throw new ArgumentException(Localize.T("Text0063"));
        profile.Arguments = ["-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))];
        profile.PostLaunchInput = string.Empty;
        var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(cwd), _host.FormalSettings?.Appearance);
        var resolved = await Task.Run(() => LaunchPathInspector.Resolve(launch.Request));
        launch = new TabLaunchConfiguration { Request = resolved, ProfileName = launch.ProfileName, Icon = launch.Icon, FontOptions = launch.FontOptions };
        if (_closing) return Localize.T("Text0064");
        var detail = Localize.F("Text0065", profile.Name, resolved.ExecutablePath, resolved.WorkingDirectory, (resolved.RunAsAdministrator ? Localize.T("Text0833") : Localize.T("Text0834"))) +
            Localize.T("Text0066") + ContextDocument.RiskReminder(command) + Localize.T("Text0067") + command;
        if (!AgentPickerControl.Review(this, Localize.T("Text0068"), detail, Localize.T("Text0069")))
        { RecordContextOperation("cancelled"); return Localize.T("Text0070"); }
        var tab = await _tabs.LaunchAsync(launch, Localize.T("Text0071") + profile.Name);
        if (tab.Session is null) return Localize.T("Text0072");
        _host.Diagnostics.Record(DiagnosticComponent.Context, DiagnosticEventKind.CommandSubmitted);
        return Localize.T("Text0073");
    }

    private async Task<McpReadResponse> ReadMcpAsync(McpReadRequest request, McpReadGrant grant, CancellationToken token)
    {
        var source = await GetContextSourceAsync(grant.SessionId, token).ConfigureAwait(false);
        if (source is null) return new("session_closed_or_missing");
        var m = source.Metadata;
        switch (request.Tool)
        {
            case "list_profiles": return McpReadResponse.Result(new { profiles = new[] { new { id = ContextText.SafeField(m.ProfileId), displayName = ContextText.SafeField(m.ProfileName), kind = m.Kind } } });
            case "list_sessions": return McpReadResponse.Result(new { sessions = new[] { new { id = m.SessionId, state = m.State, profileId = ContextText.SafeField(m.ProfileId) } } });
            case "get_session_metadata":
                var fields = new Dictionary<string, object?> { ["id"] = m.SessionId, ["state"] = m.State, ["profileId"] = ContextText.SafeField(m.ProfileId), ["kind"] = m.Kind,
                    ["verifiedLaunchProcessName"] = ContextText.SafeField(m.ProcessName), ["agentTaskState"] = "unknown" };
                if (grant.Fields.HasFlag(McpReadFields.WorkingDirectory)) { fields["cwd"] = ContextText.SafeField(m.WorkingDirectory, 4096); fields["cwdSource"] = m.DirectorySource; }
                return McpReadResponse.Result(fields);
            case "get_output_tail":
            case "get_selection":
                var selection = request.Tool == "get_selection";
                Core.Agents.ContextSnapshot snapshot;
                try
                {
                    snapshot = await _contexts.CollectAsync(new() { SessionId = grant.SessionId, PreferSelection = selection, SelectionOnly = selection,
                        MaxLines = request.MaxLines, MaxBytes = request.MaxBytes }, token).ConfigureAwait(false);
                }
                catch (InvalidOperationException) { return new("session_closed_or_missing"); }
                if (selection && !snapshot.HasSelection) return new("no_selection");
                return McpReadResponse.Result(new { text = snapshot.Text, truncated = snapshot.Truncated, redactions = snapshot.Redactions,
                    utf8Bytes = Encoding.UTF8.GetByteCount(snapshot.Text), maxLines = Math.Min(request.MaxLines, ContextLimits.MaxLines), maxBytes = Math.Min(request.MaxBytes, ContextLimits.MaxBytes), untrusted = true });
            default: return new("unknown_tool");
        }
    }

    private void ShowMcpAccess()
    {
        if (_mcpWindow is not null) { _mcpWindow.Activate(); return; }
        _mcpWindow = new(this, _mcp, () => _tabs.Tabs.Where(t => t.Session is not null && t.State != TabRuntimeState.Closing)
            .Select(t => new McpSessionChoice(t.Session!.Id, $"{ContextText.SafeField(t.ProfileName)} · {t.Session.Id}")).ToArray(), CreateMcpConfigurationAsync);
        _mcpWindow.Closed += (_, _) => _mcpWindow = null;
        _mcpWindow.Show();
    }

    private Task<string> CreateMcpConfigurationAsync() => Task.Run(() =>
    {
        if (_host.Paths.Mode == Core.Data.AppDataMode.Msix) return Localize.T("Text0074");
        var executable = Path.Combine(AppContext.BaseDirectory, "Mcp", "ArkTerminal.Mcp.exe");
        if (!File.Exists(executable) || Environment.ProcessPath is not { } image || !Path.GetFileName(image).Equals("ArkTerminal.exe", StringComparison.OrdinalIgnoreCase))
            return Localize.T("Text0075");
        using var process = Process.GetCurrentProcess();
        var entry = new { command = executable, args = new[] { "--pipe", _mcp.PipeName, "--instance", _mcp.InstanceId.ToString(), "--ui-pid", Environment.ProcessId.ToString(),
            "--ui-start", process.StartTime.ToUniversalTime().Ticks.ToString(), "--ui-image", image } };
        return JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["ark-terminal-readonly"] = entry } }, new JsonSerializerOptions { WriteIndented = true });
    });

    private void OpenContext_Click(object sender, RoutedEventArgs e) => OpenContext(0);
    private void OpenSnapshot_Click(object sender, RoutedEventArgs e) => OpenContext(3);
    private void OpenCommandReview_Click(object sender, RoutedEventArgs e) => OpenCommandReview();
    private void McpAccess_Click(object sender, RoutedEventArgs e) => ShowMcpAccess();
}
