using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Infrastructure;
using ArkTerminal.Terminal.Integration;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Sessions.Windows;

namespace ArkTerminal;

public partial class MainWindow
{
    private readonly ConcurrentQueue<(ConPtySession Session, ShellIntegrationEventArgs Signal)> _shellSignals = new();
    private readonly ShellNotificationPolicy _notificationPolicy = new();
    private ShellNotificationService? _shellNotifications;
    private int _shellRefreshPending;
    private bool _recordingBusy;

    private void InitializeStageSeven()
    {
        _tabs.ConfirmElevatedLaunch = ConfirmElevatedLaunchAsync;
        _tabs.SessionAttached += session =>
        {
            session.ShellIntegrationChanged += (_, args) =>
            {
                if (args.Kind is ShellEventKind.Completed or ShellEventKind.Prompt && _shellSignals.Count < 64) _shellSignals.Enqueue((session, args));
                QueueShellRefresh();
            };
            session.StateChanged += (_, args) =>
            {
                if (args.State is SessionState.Exited or SessionState.Failed or SessionState.Disposed)
                { _notificationPolicy.Remove(session.Id); QueueShellRefresh(); }
            };
        };
        _host.CommandHistory.Changed += QueueShellRefresh;
        _host.Transcripts.Changed += _ => QueueShellRefresh();
        TerminalView.LinkRequested += OpenTerminalLink;
        SourceInitialized += (_, _) => _shellNotifications = new(new WindowInteropHelper(this).Handle);
        Activated += (_, _) => _shellNotifications?.Dispose();
        Closed += (_, _) => _shellNotifications?.Dispose();
    }

    private void QueueShellRefresh()
    {
        if (_allowClose || Dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _shellRefreshPending, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _shellRefreshPending, 0);
            while (_shellSignals.TryDequeue(out var item))
            {
                if (!_tabs.Tabs.Any(tab => tab.Session == item.Session && tab.Session.State == SessionState.Running)) continue;
                var settings = _host.FormalSettings?.ShellNotifications ?? new();
                var kind = _notificationPolicy.Evaluate(item.Session.Id, item.Signal, ShellNotificationService.IsApplicationFocused, settings, DateTimeOffset.UtcNow);
                if (kind is not null)
                {
                    var message = kind switch
                    {
                        ShellNotificationKind.LongTask => "一个长任务已结束。返回终端查看结果。",
                        ShellNotificationKind.CommandCompleted => "一个 Shell 命令已完成。返回终端查看结果。",
                        _ => "Shell 已回到提示符，正在等待输入。"
                    };
                    if (_shellNotifications?.Show(message) == false) StatusText.Text = "Windows 未接受本次通知；可返回终端查看任务结果。";
                }
            }
            UpdateShellDisplay();
        });
    }

    private void UpdateShellDisplay()
    {
        if (!_stageSixReady) return;
        var session = _tabs.ActiveTab.Session;
        var shell = session?.ShellStatus;
        var transcript = session is null ? null : _host.Transcripts.GetStatus(session.Id);
        if (shell is not null)
        {
            var state = shell.State switch { ShellCommandState.Ready => "等待命令", ShellCommandState.Running => "命令运行中", _ => "命令状态未知" };
            var code = shell.ExitCode is { } exit ? $" · 退出码 {exit}" : shell.Succeeded is { } success ? success ? " · 成功（退出码未知）" : " · 失败（退出码未知）" : string.Empty;
            TerminalStatusText.Text = $"{_tabs.ActiveTab.StatusText} · {(_tabs.ActiveTab.Launch?.Request.RunAsAdministrator == true ? "管理员 · " : string.Empty)}{state}{code} · {shell.WorkingDirectory ?? "cwd 未知"}";
            TerminalStatusText.ToolTip = shell.Detail + "\nShell 信号仅供显示，不能授权执行或读取文件。\nCtrl+单击链接查看完整目标。";
        }
        if (_tabs.ActiveTab.Launch?.Request.AgentRecipe is { } agent)
        {
            TerminalStatusText.Text = $"{_tabs.ActiveTab.StatusText} · {agent.AgentId} · {_tabs.ActiveTab.AgentBackendDescription} · Agent 执行 / 等待状态：未知";
            TerminalStatusText.ToolTip = "进程生命周期与 Agent 任务状态独立。没有可靠 Agent 事件，不从输出、Shell 存活或按键猜测忙碌与等待。";
        }
        CommandDurationText.Text = shell?.Duration is { } duration ? $"耗时：{duration.TotalSeconds:0.###} s" : "耗时：未知";
        TranscriptStatusText.Text = transcript?.Recording == true ? transcript.Truncated ? "● 转录中 · 已截断" : "● 转录中" :
            transcript?.Message.Contains("失败", StringComparison.Ordinal) == true ? "转录停止 · 写入失败" : transcript?.Truncated == true ? "转录停止 · 已截断" : string.Empty;
        TranscriptStatusText.ToolTip = transcript?.Message;
        ToggleTranscriptButton.IsEnabled = session?.State == SessionState.Running && !_recordingBusy;
        ToggleTranscriptButton.Content = transcript?.Recording == true ? "停止转录" : "开始转录";
        HistoryEnabledInput.IsChecked = _host.CommandHistory.Enabled;
        TranscriptionEnabledInput.IsChecked = transcript?.Recording == true;
        TranscriptionEnabledInput.IsEnabled = session?.State == SessionState.Running && !_recordingBusy;
        var policy = _host.FormalSettings?.Transcription ?? new();
        RecordingStatusText.Text = _host.CommandHistory.Status + "\n" + (transcript?.Message ?? "当前没有运行会话。") +
            "\n历史上限：" + (_host.FormalSettings?.History.MaxEntries ?? 1000) + " 条 / " + (_host.FormalSettings?.History.MaxSizeBytes ?? 1048576) +
            $" 字节；转录：每会话 {policy.MaxFileSizeBytes:N0} 字节 / 总量 {policy.MaxTotalSizeBytes:N0} 字节 / {policy.RetentionDays} 天。";
    }

    private async Task<SessionLaunchRequest?> ConfirmElevatedLaunchAsync(SessionLaunchRequest request)
    {
        var approved = await Task.Run(() => LaunchPathInspector.Resolve(request));
        var text = new StringBuilder("将通过 Windows UAC 创建一个嵌入式管理员会话。关闭标签或窗口会结束该会话及其进程树。\n\n")
            .Append("程序：").Append(approved.ExecutablePath).Append("\n目录：").Append(approved.WorkingDirectory)
            .Append("\n模式：管理员 Shell\n参数（逐项）：\n");
        foreach (var argument in approved.Arguments) text.Append(System.Text.Json.JsonSerializer.Serialize(argument)).Append('\n');
        text.Append("\n环境覆盖：\n");
        text.Append(request.Environment.Count).Append(" 项（值不显示；可能包含凭据）\n");
        text.Append("\n启动后输入：").Append(System.Text.Json.JsonSerializer.Serialize(approved.PostLaunchInput));
        if (text.Length > 12000) throw new ArgumentException("管理员启动详情过长，无法完整展示；请缩短参数或启动后输入。");
        return MessageBox.Show(this, text.ToString(), "确认本次管理员启动", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes ? approved : null;
    }

    private async void OpenTerminalLink(TerminalLink link)
    {
        var action = link.IsWeb ? "用默认浏览器打开" : "在资源管理器中打开目录或定位文件";
        if (MessageBox.Show(this, $"显示文本：{link.DisplayText}\n\n完整目标：\n{link.Target}\n\n{action}？", "确认链接目标", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (link.IsWeb) await Task.Run(() => Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true }));
            else
            {
                var attributes = await Task.Run(() => File.GetAttributes(link.Target));
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
                if (!attributes.HasFlag(FileAttributes.Directory)) info.Arguments = "/select,\"" + link.Target + "\"";
                else info.ArgumentList.Add(link.Target);
                await Task.Run(() => Process.Start(info));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { StatusText.Text = $"无法打开目标：{link.Target} · {error.Message}"; }
    }

    private async void HistoryEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingBusy) return;
        var enable = HistoryEnabledInput.IsChecked == true;
        if (enable && MessageBox.Show(this, "记录本次运行中具有可靠开始/结束事件的命令文本、目录与结果。可能含秘密；不自动脱敏，不导入 Shell 历史。重启后关闭。是否开启？", "开启应用历史", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { UpdateShellDisplay(); return; }
        await _host.CommandHistory.SetEnabledAsync(enable, _host.FormalSettings?.History ?? new());
        UpdateShellDisplay();
    }
    private async void ToggleTranscript_Click(object sender, RoutedEventArgs e)
    {
        var session = _tabs.ActiveTab.Session;
        if (session?.State != SessionState.Running || _recordingBusy) return;
        var enable = !_host.Transcripts.GetStatus(session.Id).Recording;
        var policy = _host.FormalSettings?.Transcription ?? new();
        if (enable && MessageBox.Show(this, $"仅记录此会话开启后的文本输出，不记录按键。程序回显可能含秘密；不保证还原光标覆盖与备用屏动态画面。\n每会话 {policy.MaxFileSizeBytes:N0} 字节、总量 {policy.MaxTotalSizeBytes:N0} 字节、{policy.RetentionDays} 天；达到上限轮换并提示截断。写盘失败或队列满时停止，终端继续运行。是否开始？", "开启当前会话转录", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { UpdateShellDisplay(); return; }
        _recordingBusy = true;
        try
        {
            if (enable) await _host.Transcripts.StartAsync(session.Id, policy);
            else await _host.Transcripts.StopAsync(session.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "转录未开启：" + error.Message; }
        finally { _recordingBusy = false; UpdateShellDisplay(); }
    }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "只清除 ArkTerminal 应用命令历史；不会清除 PowerShell、CMD 或 Bash 自身历史。继续？", "清除应用历史", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await _host.CommandHistory.ClearAsync(); StatusText.Text = "应用命令历史已清除。"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "清除历史失败：" + error.Message; }
    }
    private async void ClearTranscripts_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "停止所有转录，并删除 ArkTerminal 拥有的输出转录文件；不会删除配置、Shell 历史或其他文件。继续？", "清除应用转录", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _host.Transcripts.ClearAsync(); StatusText.Text = "应用转录已停止并清除。"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = "清除转录失败：" + error.Message; }
    }
    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        var query = new TextBox { Margin = new(0, 0, 0, 8) };
        var results = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var search = new Button { Content = "搜索已保存历史", Margin = new(0, 0, 0, 8) };
        var panel = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(query, Dock.Top); DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(query); panel.Children.Add(search); panel.Children.Add(results);
        var window = new Window { Owner = this, Title = "ArkTerminal 应用历史 · 最多显示 100 条匹配", Width = 780, Height = 580, Content = panel };
        window.SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        window.SetResourceReference(ForegroundProperty, "Theme.PrimaryText");
        async Task Load()
        {
            search.IsEnabled = false;
            try
            {
                var entries = await _host.CommandHistory.ReadAsync(query.Text);
                results.Text = string.Join("\n\n", entries.Take(100).Select(item => $"{item.TimeUtc.ToLocalTime():g} · {item.Shell} · 退出码 {item.ExitCode?.ToString() ?? "未知"} · {item.DurationMilliseconds / 1000:0.###} s\n{item.WorkingDirectory}\n{item.Command}"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { results.Text = "读取历史失败：" + error.Message; }
            finally { search.IsEnabled = true; }
        }
        search.Click += async (_, _) => await Load();
        window.Loaded += async (_, _) => await Load();
        window.Show();
    }
}
