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
                if (connections.Any(c => c.Status == "等待 UI 授权" && _mcpNotified.Add(c.Id)))
                {
                    StatusText.Text = "有本机 MCP 连接等待披露授权；默认拒绝读取。可从应用菜单管理。";
                    if (!_closing && _mcpWindow is null) ShowMcpAccess();
                }
            });
        };
        _mcp.Start();
        void Add(string id, string title, Func<Task> action) => _commands.Register(new CommandDescriptor
        { Id = id, Title = title, Category = "上下文 / MCP", Keywords = "ark-terminal-context Skill 上下文 错误 快照 解释 危险 修复 交接 MCP",
            Shortcut = id == "ark-terminal-context" ? "Ctrl+Shift+F9（全局）" : "未绑定", ExecuteAsync = _ => action() });
        Task Open(int purpose) { OpenContext(purpose); return Task.CompletedTask; }
        Add("ark-terminal-context", "上下文：预览 / 复制 / 跨 Agent 交接", () => Open(0));
        Add("context.snapshot", "保存错误快照 Markdown（先预览）", () => Open(3));
        Add("context.explain", "请求 CLI 解释命令与危险提醒", () => Open(1));
        Add("context.repair", "请求 CLI 分析错误与修复建议", () => Open(2));
        Add("context.review-command", "审阅 Agent 建议 / 确认执行命令", () => { OpenCommandReview(); return Task.CompletedTask; });
        Add("mcp.manage", "只读 MCP：连接授权 / 撤销 / 复制配置", () => { ShowMcpAccess(); return Task.CompletedTask; });
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
            shell.WorkingDirectory ?? tab.WorkingDirectory, shell.WorkingDirectory is null ? "启动快照；当前目录未验证" : "Shell 集成元数据（不可信）",
            session.VerifiedLaunchProcessName ?? "未知", session.State.ToString());
        return new TerminalContextSource(session, metadata, TerminalView.GetContextSelection(id));
    }, System.Windows.Threading.DispatcherPriority.Background, token).Task;

    private void OpenContext(int purpose)
    {
        if (_closing || _allowClose) return;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
        if (_contextWindow is not null) { _contextWindow.Activate(); return; }
        var tab = _tabs.ActiveTab;
        if (tab.Session is not { } session || tab.Launch is null) { StatusText.Text = "请先选择一个已有终端会话，再采集上下文。"; return; }
        var targets = new List<ContextTarget> { new("剪贴板 / 手工选择 CLI") };
        targets.AddRange(_tabs.Tabs.Where(t => t.Launch?.Request.AgentRecipe is not null).Select(t => new ContextTarget(
            $"Agent 标签：{ContextText.SafeField(t.ProfileName)} · {t.Session?.Id} · 启动 cwd：{ContextText.SafeField(t.WorkingDirectory, 4096)} · 输入模式未知", t.Launch!.Clone(), t.Session?.Id)));
        targets.AddRange((_host.Catalog?.Profiles ?? []).Where(p => p.AgentRecipe is not null).Select(p => new ContextTarget(
            "新辅助 Agent 配方：" + ContextText.SafeField(p.Name) + " · cwd：" + ContextText.SafeField(p.WorkingDirectory, 4096),
            TabLaunchConfiguration.FromProfile(p.DeepClone(), ExpandHome(p.WorkingDirectory), _host.FormalSettings?.Appearance))));
        _contextWindow = new(this, session.Id, _contexts, _snapshots, targets,
            () => !_closing && _tabs.Tabs.Any(t => t.Session == session && t.State != TabRuntimeState.Closing),
            async target =>
            {
                if (target.ExistingSessionId is { } targetId && !_tabs.Tabs.Any(t => t.Session?.Id == targetId)) throw new InvalidOperationException("原目标会话已关闭，请重新选择目标。");
                var auxiliary = await _tabs.LaunchAsync(target.AgentLaunch!.Clone(), "辅助 · " + target.AgentLaunch.ProfileName);
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
        if (command.Length > ContextLimits.MaxCommandCharacters) throw new ArgumentException("命令超过 8192 字符。");
        profile.Arguments = ["-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))];
        profile.PostLaunchInput = string.Empty;
        var launch = TabLaunchConfiguration.FromProfile(profile, ExpandHome(cwd), _host.FormalSettings?.Appearance);
        var resolved = await Task.Run(() => LaunchPathInspector.Resolve(launch.Request));
        launch = new TabLaunchConfiguration { Request = resolved, ProfileName = launch.ProfileName, Icon = launch.Icon, FontOptions = launch.FontOptions };
        if (_closing) return "已取消，未创建会话。";
        var detail = $"目标：新建 {profile.Name} 会话\n实际程序：{resolved.ExecutablePath}\n实际 cwd：{resolved.WorkingDirectory}\n权限：{(resolved.RunAsAdministrator ? "管理员；仍需 UAC" : "普通")}\n" +
            "使用 -NoProfile -NoExit -EncodedCommand 执行下方完整命令。\n\n" + ContextDocument.RiskReminder(command) + "\n\n实际命令：\n" + command;
        if (!AgentPickerControl.Review(this, "单独确认执行建议中的命令", detail, "在新会话执行此命令"))
        { RecordContextOperation("cancelled"); return "已取消；未提交或执行命令。"; }
        var tab = await _tabs.LaunchAsync(launch, "建议命令 · " + profile.Name);
        if (tab.Session is null) return "会话未启动，命令未提交。";
        _host.Diagnostics.Record(DiagnosticComponent.Context, DiagnosticEventKind.CommandSubmitted);
        return "命令已作为新 Shell 的启动命令提交；执行结果请查看该终端，当前未确认成功。";
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
        if (_host.Paths.Mode == Core.Data.AppDataMode.Msix) return "MSIX：尚无经过实际安装 / 清单验证的 stdio 启动入口，配置复制暂不可用。阶段 10 需在真实安装包中复验；不能把此限制当成 MSIX 验收通过。";
        var executable = Path.Combine(AppContext.BaseDirectory, "Mcp", "ArkTerminal.Mcp.exe");
        if (!File.Exists(executable) || Environment.ProcessPath is not { } image || !Path.GetFileName(image).Equals("ArkTerminal.exe", StringComparison.OrdinalIgnoreCase))
            return "当前没有可验证的 ArkTerminal.exe / Mcp/ArkTerminal.Mcp.exe 入口。请从完整构建或 ZIP 目录启动。";
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
