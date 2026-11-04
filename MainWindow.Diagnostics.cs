using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ArkTerminal.Commands;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Visuals;

namespace ArkTerminal;

public partial class MainWindow
{
    private readonly DispatcherTimer _performanceTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _performanceSampling;
    private int _performanceVersion;
    private long _previousSampleTime;
    private long _previousFrames;
    private long _previousRedraws;
    private long _previousRead;
    private long _previousWritten;
    private string _previousSessions = string.Empty;
    private bool _performanceTimerAttached;

    private void InitializeStageSixCommands()
    {
        void Add(string id, string title, string shortcut, CommandScope scope, ShortcutGesture[] gestures, Func<Task> execute, Func<bool>? available = null)
            => _commands.Register(new CommandDescriptor { Id = id, Title = title, Category = scope == CommandScope.Terminal ? "终端" : "工作台", Shortcut = shortcut,
                Scope = scope, Gestures = gestures, CanExecute = available ?? (() => true), ExecuteAsync = _ => execute() });
        Task Action(Action action) { action(); return Task.CompletedTask; }
        bool TerminalAvailable() => TerminalView.IsVisible && _tabs.ActiveTab.Session is not null;
        const ModifierKeys control = ModifierKeys.Control;
        const ModifierKeys both = ModifierKeys.Control | ModifierKeys.Shift;
        Add("workbench.new", "新建页", "Ctrl+Shift+T", CommandScope.Workbench, [new(Key.T, both)], () => Action(() => NewPage_Click(this, new())));
        Add("workbench.settings", "打开设置", "未绑定", CommandScope.Workbench, [], () => Action(ShowSettings));
        Add("workbench.palette", "命令面板", "可配置的全局热键", CommandScope.Workbench, [], () => Action(ShowCommandPalette));
        Add("workbench.focus", "切换专注模式", "可配置的全局热键", CommandScope.Workbench, [], () => Action(ToggleFocusMode));
        Add("workbench.performance", "切换性能模式", "未绑定", CommandScope.Workbench, [], () => Action(TogglePerformanceMode));
        Add("workbench.metrics", "显示 / 隐藏性能面板", "未绑定", CommandScope.Workbench, [], () => Action(TogglePerformancePanel));
        Add("workbench.diagnostics", "预览诊断导出", "未绑定", CommandScope.Workbench, [], PreviewDiagnosticsAsync);
        Add("tab.next", "下一个标签", "Ctrl+Tab", CommandScope.Workbench, [new(Key.Tab, control)], () => Action(() => _tabs.Next(1)));
        Add("tab.previous", "上一个标签", "Ctrl+Shift+Tab", CommandScope.Workbench, [new(Key.Tab, both)], () => Action(() => _tabs.Next(-1)));
        Add("tab.close", "关闭标签", "Ctrl+Shift+W", CommandScope.Workbench, [new(Key.W, both)], () => CloseTabAsync(_tabs.ActiveTab), () => !_tabs.ActiveTab.IsNewPage);
        Add("tab.duplicate", "重复标签（原启动快照）", "未绑定", CommandScope.Workbench, [], () => DuplicateAsync(_tabs.ActiveTab), () => _tabs.ActiveTab.Launch is not null);
        Add("tab.restart", "重启会话（最新正式 Profile）", "未绑定", CommandScope.Workbench, [], () => RestartAsync(_tabs.ActiveTab), () => _tabs.ActiveTab.Launch is not null);
        Add("terminal.copy", "复制有效选区", "Ctrl+Shift+C", CommandScope.Terminal, [new(Key.C, both)], TerminalView.CopySelectionAsync, TerminalAvailable);
        Add("terminal.paste", "粘贴原文", "Ctrl+Shift+V", CommandScope.Terminal, [new(Key.V, both)], TerminalView.PasteAsync, TerminalAvailable);
        Add("terminal.selectAll", "全选当前缓冲", "Ctrl+Shift+A", CommandScope.Terminal, [new(Key.A, both)], TerminalView.SelectAllAsync, TerminalAvailable);
        Add("terminal.search", "搜索", "Ctrl+F", CommandScope.Terminal, [new(Key.F, control)], () => Action(TerminalView.RequestSearch), TerminalAvailable);
        Add("terminal.interrupt", "中断（Ctrl+C 保留 Shell 语义）", "Ctrl+C", CommandScope.Terminal, [],
            () => _tabs.ActiveTab.Session!.WriteInputAsync((_tabs.ActiveTab.Session.Win32InputModeEnabled ? Terminal.Input.TerminalInputEncoder.EncodeWin32ControlCharacter('C') : "\u0003").AsMemory(), CancellationToken.None), () => TerminalAvailable() && _tabs.ActiveTab.Session?.State == SessionState.Running);
        Add("terminal.pageUp", "向上滚动一页", "Shift+PageUp", CommandScope.Terminal, [new(Key.PageUp, ModifierKeys.Shift)], () => Action(() => TerminalView.ScrollPages(1)), TerminalAvailable);
        Add("terminal.pageDown", "向下滚动一页", "Shift+PageDown", CommandScope.Terminal, [new(Key.PageDown, ModifierKeys.Shift)], () => Action(() => TerminalView.ScrollPages(-1)), TerminalAvailable);
        Add("terminal.top", "回滚顶部", "Ctrl+Shift+Home", CommandScope.Terminal, [new(Key.Home, both)], () => Action(TerminalView.ScrollToTop), TerminalAvailable);
        Add("terminal.bottom", "回到最新", "Ctrl+Shift+End", CommandScope.Terminal, [new(Key.End, both)], () => Action(TerminalView.ScrollToBottom), TerminalAvailable);
        Add("terminal.clearView", "清屏视图（不发 Shell 命令）", "Ctrl+Shift+K", CommandScope.Terminal, [new(Key.K, both)], () => Action(() => TerminalView.ClearBuffer(false)), TerminalAvailable);
        Add("terminal.clearHistory", "清除回滚（保留可见内容）", "未绑定", CommandScope.Terminal, [], () => Action(() => TerminalView.ClearBuffer(true)), TerminalAvailable);
        Add("terminal.zoomIn", "放大字号", "Ctrl++ / Ctrl+滚轮向上", CommandScope.Terminal, [new(Key.OemPlus, control), new(Key.OemPlus, both), new(Key.Add, control)], () => Action(() => TerminalView.Zoom(1)), TerminalAvailable);
        Add("terminal.zoomOut", "缩小字号", "Ctrl+- / Ctrl+滚轮向下", CommandScope.Terminal, [new(Key.OemMinus, control), new(Key.Subtract, control)], () => Action(() => TerminalView.Zoom(-1)), TerminalAvailable);
        Add("terminal.zoomReset", "恢复配置字号", "Ctrl+0", CommandScope.Terminal, [new(Key.D0, control), new(Key.NumPad0, control)], () => Action(TerminalView.ResetZoom), TerminalAvailable);
        ShortcutList.ItemsSource = _commands.Search(string.Empty).Where(command => !command.Id.StartsWith("tab.activate.", StringComparison.Ordinal) && !command.Id.StartsWith("profile.launch.", StringComparison.Ordinal)).ToArray();
        _performanceTimer.Tick += async (_, _) => await SamplePerformanceAsync();
        _performanceTimerAttached = true;
    }

    private async Task ExecuteCommandAsync(CommandDescriptor command)
    {
        if (!command.CanExecute()) { StatusText.Text = "当前焦点/会话状态下不可执行此命令。"; return; }
        try { await command.ExecuteAsync(CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _host.Diagnostics.Record(Core.Diagnostics.DiagnosticComponent.Terminal, Core.Diagnostics.DiagnosticEventKind.InputFailed, exception.HResult);
            StatusText.Text = "命令未完成：" + exception.Message;
        }
    }

    private void PerformancePanel_Click(object sender, RoutedEventArgs e) => TogglePerformancePanel();
    private void TogglePerformancePanel()
    {
        if (PerformancePanel.Visibility == Visibility.Visible && PerformancePanel.IsHitTestVisible) MotionPolicy.Hide(PerformancePanel);
        else MotionPolicy.Show(PerformancePanel);
        _previousSampleTime = 0;
        UpdatePerformanceSampling();
    }

    private void UpdatePerformanceSampling()
    {
        if (!_stageSixReady || !_performanceTimerAttached) return;
        _performanceVersion++;
        if (PerformancePanel.IsVisible && PerformancePanel.IsHitTestVisible && WindowState != WindowState.Minimized && IsVisible && !_visuals.IsPresentationSuspended)
        {
            _performanceTimer.Start();
            _ = SamplePerformanceAsync();
        }
        else { _performanceTimer.Stop(); _previousSampleTime = 0; }
    }

    private async Task SamplePerformanceAsync()
    {
        if (_performanceSampling) return;
        if (!PerformancePanel.IsVisible || !PerformancePanel.IsHitTestVisible || WindowState == WindowState.Minimized || _visuals.IsPresentationSuspended) { UpdatePerformanceSampling(); return; }
        _performanceSampling = true;
        var version = _performanceVersion;
        try
        {
            var text = await CapturePerformanceAsync(true);
            if (version == _performanceVersion && PerformancePanel.IsVisible && PerformancePanel.IsHitTestVisible) PerformanceText.Text = text;
        }
        finally { _performanceSampling = false; }
    }

    private async Task<string> CapturePerformanceAsync(bool updateRates)
    {
        var frames = TerminalView.PublishedFrames;
        var redraws = TerminalView.RedrawRequests;
        var shared = TerminalView.EstimatedSharedTextureBytes + FontPreview.EstimatedSharedTextureBytes + _profileEditor.FontPreview.EstimatedSharedTextureBytes;
        var pending = TerminalView.RenderPending;
        var sessions = _host.Sessions.Sessions.OfType<ConPtySession>().ToArray();
        var result = await Task.Run(() =>
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var rows = sessions.Select(session => (session.State, Io: session.Diagnostics, Buffer: session.BufferStatistics)).ToArray();
            return (Private: process.PrivateMemorySize64, Working: process.WorkingSet64, Handles: process.HandleCount, Rows: rows);
        });
        var read = result.Rows.Sum(row => row.Io.BytesRead);
        var written = result.Rows.Sum(row => row.Io.InputBytesWritten);
        var now = Stopwatch.GetTimestamp();
        var sessionKey = string.Join(',', sessions.Select(session => session.Id));
        var rates = "采样率：等待下一次采样；帧为成功发布至 D3DImage 的终端帧，不代表显示器刷新率。";
        if (_previousSampleTime != 0 && sessionKey == _previousSessions)
        {
            var seconds = (now - _previousSampleTime) / (double)Stopwatch.Frequency;
            rates = FormattableString.Invariant($"终端发布帧 {Math.Max(0, frames - _previousFrames) / seconds:F1} FPS；重绘请求 {Math.Max(0, redraws - _previousRedraws) / seconds:F1}/s；实际 RX {Math.Max(0, read - _previousRead) / seconds:F0} B/s，TX {Math.Max(0, written - _previousWritten) / seconds:F0} B/s");
        }
        if (updateRates)
        {
            _previousSampleTime = now; _previousFrames = frames; _previousRedraws = redraws; _previousRead = read; _previousWritten = written; _previousSessions = sessionKey;
        }
        var text = new StringBuilder(rates).AppendLine();
        text.AppendLine(FormattableString.Invariant($"ArkTerminal 进程（不含 Shell 子进程）：私有 {result.Private / 1048576d:F1} MiB；工作集 {result.Working / 1048576d:F1} MiB；句柄 {result.Handles}"));
        text.AppendLine(FormattableString.Invariant($"应用图形：共享纹理容量估算 {shared / 1048576d:F2} MiB（共享表面计一次；不含驱动/设备开销）；待渲染 {pending}"));
        text.AppendLine($"累计：发布帧 {frames}；重绘请求 {redraws}；RX {read} B；TX {written} B。无独立输出解析队列，输出在会话读取线程解析。");
        for (var index = 0; index < result.Rows.Length; index++)
        {
            var row = result.Rows[index];
            text.AppendLine(FormattableString.Invariant($"会话 {index + 1} [{row.State}]：输入队列 {row.Io.InputQueueDepth}/{row.Io.InputQueueCapacity} 项；resize 待处理 {row.Io.ResizePending}；网格 {row.Buffer.Columns}×{row.Buffer.Rows}；回滚 {row.Buffer.ScrollbackLines}/{row.Buffer.ScrollbackCapacity} 行，存储估算 {row.Buffer.EstimatedScrollbackBytes / 1048576d:F2}/{row.Buffer.MaxScrollbackBytes / 1048576d:F0} MiB；单元格估算 {row.Buffer.EstimatedCellStorageBytes / 1048576d:F2} MiB"));
        }
        text.Append("会话估算 = 已分配单元格槽位 × 64 B，排除可变字簇文本、CLR/行数组及共享图形开销；不是精确会话内存。隐藏面板停止周期采样。");
        return text.ToString();
    }

    private async void PreviewDiagnostics_Click(object sender, RoutedEventArgs e) => await PreviewDiagnosticsAsync();
    private async Task PreviewDiagnosticsAsync()
    {
        try
        {
            var performance = await CapturePerformanceAsync(false);
            var events = await _host.Diagnostics.ReadRecentAsync();
            var visual = _visuals.Diagnostics;
            var text = new StringBuilder("ArkTerminal diagnostic preview\n");
            text.AppendLine($"Version: {typeof(App).Assembly.GetName().Version}; OS: {Environment.OSVersion.Version}; .NET: {Environment.Version}; Mode: {_host.Paths.Mode}");
            text.AppendLine("Data: <DATA_ROOT>; Home: <HOME>; Config: <DATA_ROOT>/Config; Diagnostics: <DATA_ROOT>/Diagnostics");
            text.AppendLine($"ConPTY: Windows build {Environment.OSVersion.Version.Build}; renderer: DirectWrite/D2D/D3DImage; material: {visual.AppliedMaterial}; desktop: {visual.DesktopBackgroundApplied}; aurora: {visual.AuroraAnimating}; performance mode: {visual.PerformanceModeEnabled}");
            text.AppendLine($"Capabilities: systemBackdrop={_visuals.SupportsSystemBackdrop}; desktopBackground={_visuals.SupportsDesktopBackground}; administrator={ArkTerminal.Terminal.Sessions.Elevation.ElevationSupport.UnavailableReason is null}; shellIntegration=true; agentLauncher=true; historyRecorder=true; transcription=true");
            text.AppendLine("Excluded: terminal text, commands/arguments, environment variables, profile names/paths, transcripts and exception messages.");
            text.AppendLine(performance);
            text.AppendLine($"Diagnostic events (fixed fields only); dropped={_host.Diagnostics.DroppedEvents}; writeFailures={_host.Diagnostics.WriteFailures}");
            foreach (var item in events) text.AppendLine($"{item.TimeUtc:O} {item.Component}/{item.Event} code={item.ErrorCode} session={item.SessionSlot?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            DiagnosticPreviewInput.Text = text.ToString();
            MotionPolicy.Show(DiagnosticPreviewPanel);
            DiagnosticPreviewInput.Focus();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { StatusText.Text = "诊断预览失败：" + exception.Message; }
    }

    private void DiagnosticPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_stageSixReady) return;
        var bytes = Encoding.UTF8.GetByteCount(DiagnosticPreviewInput.Text);
        DiagnosticSizeText.Text = $"UTF-8：{bytes:N0} / {_host.Diagnostics.Options.MaxExportBytes:N0} 字节；确认后保存至当前数据根的 Diagnostics。";
        ConfirmDiagnosticButton.IsEnabled = bytes <= _host.Diagnostics.Options.MaxExportBytes;
    }

    private async void ConfirmDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _host.Diagnostics.ExportAsync(DiagnosticPreviewInput.Text);
            MotionPolicy.Hide(DiagnosticPreviewPanel, DiagnosticPreviewInput.Clear);
            StatusText.Text = "诊断已导出：" + path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { StatusText.Text = "导出失败，预览已保留：" + exception.Message; }
    }

    private void CancelDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        MotionPolicy.Hide(DiagnosticPreviewPanel, DiagnosticPreviewInput.Clear);
        StatusText.Text = "诊断预览已取消，未创建导出文件。";
    }

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_host.Paths.RootDirectory) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception) { StatusText.Text = exception.Message; }
    }

    private async void ClearDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "删除当前数据根中应用拥有的诊断日志与导出？不会删除配置、背景或会话正文。", "清理诊断", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await _host.Diagnostics.ClearAsync(); StatusText.Text = "诊断已清理；后续必要错误仍会记录固定事件。"; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { StatusText.Text = "清理失败：" + exception.Message; }
    }
}
