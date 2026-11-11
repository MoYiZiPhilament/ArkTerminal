using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
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
            => _commands.Register(new CommandDescriptor { Id = id, Title = title, Category = scope == CommandScope.Terminal ? Localize.T("Text0076") : Localize.T("Text0077"), Shortcut = shortcut,
                Scope = scope, Gestures = gestures, CanExecute = available ?? (() => true), ExecuteAsync = _ => execute() });
        Task Action(Action action) { action(); return Task.CompletedTask; }
        bool TerminalAvailable() => TerminalView.IsVisible && _tabs.ActiveTab.Session is not null;
        const ModifierKeys control = ModifierKeys.Control;
        const ModifierKeys both = ModifierKeys.Control | ModifierKeys.Shift;
        Add("workbench.new", Localize.T("Text0078"), "Ctrl+Shift+T", CommandScope.Workbench, [new(Key.T, both)], () => Action(() => NewPage_Click(this, new())));
        Add("workbench.settings", Localize.T("Text0079"), Localize.T("Text0047"), CommandScope.Workbench, [], () => Action(ShowSettings));
        Add("workbench.palette", Localize.T("Text0080"), Localize.T("Text0081"), CommandScope.Workbench, [], () => Action(ShowCommandPalette));
        Add("workbench.focus", Localize.T("Text0082"), Localize.T("Text0081"), CommandScope.Workbench, [], () => Action(ToggleFocusMode));
        Add("workbench.performance", Localize.T("Text0083"), Localize.T("Text0047"), CommandScope.Workbench, [], () => Action(TogglePerformanceMode));
        Add("workbench.metrics", Localize.T("Text0084"), Localize.T("Text0047"), CommandScope.Workbench, [], () => Action(TogglePerformancePanel));
        Add("workbench.diagnostics", Localize.T("Text0085"), Localize.T("Text0047"), CommandScope.Workbench, [], PreviewDiagnosticsAsync);
        Add("tab.next", Localize.T("Text0086"), "Ctrl+Tab", CommandScope.Workbench, [new(Key.Tab, control)], () => Action(() => NavigateTabs(1)));
        Add("tab.previous", Localize.T("Text0087"), "Ctrl+Shift+Tab", CommandScope.Workbench, [new(Key.Tab, both)], () => Action(() => NavigateTabs(-1)));
        Add("tab.close", Localize.T("Text0088"), "Ctrl+Shift+W", CommandScope.Workbench, [new(Key.W, both)], () => CloseTabAsync(_tabs.ActiveTab), () => !_tabs.ActiveTab.IsNewPage);
        Add("tab.duplicate", Localize.T("Text0089"), Localize.T("Text0047"), CommandScope.Workbench, [], () => DuplicateAsync(_tabs.ActiveTab), () => _tabs.ActiveTab.Launch is not null);
        Add("tab.restart", Localize.T("Text0090"), Localize.T("Text0047"), CommandScope.Workbench, [], () => RestartAsync(_tabs.ActiveTab), () => _tabs.ActiveTab.Launch is not null);
        Add("terminal.copy", Localize.T("Text0091"), "Ctrl+Shift+C", CommandScope.Terminal, [new(Key.C, both)], TerminalView.CopySelectionAsync, TerminalAvailable);
        Add("terminal.paste", Localize.T("Text0092"), "Ctrl+Shift+V", CommandScope.Terminal, [new(Key.V, both)], TerminalView.PasteAsync, TerminalAvailable);
        Add("terminal.selectAll", Localize.T("Text0093"), "Ctrl+Shift+A", CommandScope.Terminal, [new(Key.A, both)], TerminalView.SelectAllAsync, TerminalAvailable);
        Add("terminal.search", Localize.T("Text0094"), "Ctrl+F", CommandScope.Terminal, [new(Key.F, control)], () => Action(TerminalView.RequestSearch), TerminalAvailable);
        Add("terminal.interrupt", Localize.T("Text0095"), "Ctrl+C", CommandScope.Terminal, [],
            () => _tabs.ActiveTab.Session!.WriteInputAsync((_tabs.ActiveTab.Session.Win32InputModeEnabled ? Terminal.Input.TerminalInputEncoder.EncodeWin32ControlCharacter('C') : "\u0003").AsMemory(), CancellationToken.None), () => TerminalAvailable() && _tabs.ActiveTab.Session?.State == SessionState.Running);
        Add("terminal.pageUp", Localize.T("Text0096"), "Shift+PageUp", CommandScope.Terminal, [new(Key.PageUp, ModifierKeys.Shift)], () => Action(() => TerminalView.ScrollPages(1)), TerminalAvailable);
        Add("terminal.pageDown", Localize.T("Text0097"), "Shift+PageDown", CommandScope.Terminal, [new(Key.PageDown, ModifierKeys.Shift)], () => Action(() => TerminalView.ScrollPages(-1)), TerminalAvailable);
        Add("terminal.top", Localize.T("Text0098"), "Ctrl+Shift+Home", CommandScope.Terminal, [new(Key.Home, both)], () => Action(TerminalView.ScrollToTop), TerminalAvailable);
        Add("terminal.bottom", Localize.T("Text0099"), "Ctrl+Shift+End", CommandScope.Terminal, [new(Key.End, both)], () => Action(TerminalView.ScrollToBottom), TerminalAvailable);
        Add("terminal.clearView", Localize.T("Text0100"), "Ctrl+Shift+K", CommandScope.Terminal, [new(Key.K, both)], () => Action(() => TerminalView.ClearBuffer(false)), TerminalAvailable);
        Add("terminal.clearHistory", Localize.T("Text0101"), Localize.T("Text0047"), CommandScope.Terminal, [], () => Action(() => TerminalView.ClearBuffer(true)), TerminalAvailable);
        Add("terminal.zoomIn", Localize.T("Text0102"), Localize.T("Text0103"), CommandScope.Terminal, [new(Key.OemPlus, control), new(Key.OemPlus, both), new(Key.Add, control)], () => Action(() => TerminalView.Zoom(1)), TerminalAvailable);
        Add("terminal.zoomOut", Localize.T("Text0104"), Localize.T("Text0105"), CommandScope.Terminal, [new(Key.OemMinus, control), new(Key.Subtract, control)], () => Action(() => TerminalView.Zoom(-1)), TerminalAvailable);
        Add("terminal.zoomReset", Localize.T("Text0106"), "Ctrl+0", CommandScope.Terminal, [new(Key.D0, control), new(Key.NumPad0, control)], () => Action(TerminalView.ResetZoom), TerminalAvailable);
        ShortcutList.ItemsSource = _commands.Search(string.Empty).Where(command => !command.Id.StartsWith("tab.activate.", StringComparison.Ordinal) && !command.Id.StartsWith("profile.launch.", StringComparison.Ordinal)).ToArray();
        _performanceTimer.Tick += async (_, _) => await SamplePerformanceAsync();
        _performanceTimerAttached = true;
    }

    private async Task ExecuteCommandAsync(CommandDescriptor command)
    {
        if (!command.CanExecute()) { StatusText.Text = Localize.T("Text0107"); return; }
        try { await command.ExecuteAsync(CancellationToken.None); }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _host.Diagnostics.Record(Core.Diagnostics.DiagnosticComponent.Terminal, Core.Diagnostics.DiagnosticEventKind.InputFailed, exception.HResult);
            StatusText.Text = Localize.T("Text0108") + exception.Message;
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
        var rates = Localize.T("Text0109");
        if (_previousSampleTime != 0 && sessionKey == _previousSessions)
        {
            var seconds = (now - _previousSampleTime) / (double)Stopwatch.Frequency;
            rates = Localize.F("Text0110", Math.Max(0, frames - _previousFrames) / seconds, Math.Max(0, redraws - _previousRedraws) / seconds, Math.Max(0, read - _previousRead) / seconds, Math.Max(0, written - _previousWritten) / seconds);
        }
        if (updateRates)
        {
            _previousSampleTime = now; _previousFrames = frames; _previousRedraws = redraws; _previousRead = read; _previousWritten = written; _previousSessions = sessionKey;
        }
        var text = new StringBuilder(rates).AppendLine();
        text.AppendLine(Localize.F("Text0111", result.Private / 1048576d, result.Working / 1048576d, result.Handles));
        text.AppendLine(Localize.F("Text0112", shared / 1048576d, pending));
        text.AppendLine(Localize.F("Text0113", frames, redraws, read, written));
        for (var index = 0; index < result.Rows.Length; index++)
        {
            var row = result.Rows[index];
            text.AppendLine(Localize.F("Text0114", index + 1, row.State, row.Io.InputQueueDepth, row.Io.InputQueueCapacity, row.Io.ResizePending, row.Buffer.Columns, row.Buffer.Rows, row.Buffer.ScrollbackLines, row.Buffer.ScrollbackCapacity, row.Buffer.EstimatedScrollbackBytes / 1048576d, row.Buffer.MaxScrollbackBytes / 1048576d, row.Buffer.EstimatedCellStorageBytes / 1048576d));
        }
        text.Append(Localize.T("Text0115"));
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
            text.AppendLine($"Version: {typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion}; OS: {Environment.OSVersion.Version}; .NET: {Environment.Version}; Mode: {_host.Paths.Mode}");
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
        { StatusText.Text = Localize.T("Text0116") + exception.Message; }
    }

    private void DiagnosticPreview_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_stageSixReady) return;
        var bytes = Encoding.UTF8.GetByteCount(DiagnosticPreviewInput.Text);
        DiagnosticSizeText.Text = Localize.F("Text0117", bytes, _host.Diagnostics.Options.MaxExportBytes);
        ConfirmDiagnosticButton.IsEnabled = bytes <= _host.Diagnostics.Options.MaxExportBytes;
    }

    private async void ConfirmDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await _host.Diagnostics.ExportAsync(DiagnosticPreviewInput.Text);
            MotionPolicy.Hide(DiagnosticPreviewPanel, DiagnosticPreviewInput.Clear);
            StatusText.Text = Localize.T("Text0118") + path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { StatusText.Text = Localize.T("Text0119") + exception.Message; }
    }

    private void CancelDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        MotionPolicy.Hide(DiagnosticPreviewPanel, DiagnosticPreviewInput.Clear);
        StatusText.Text = Localize.T("Text0120");
    }

    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_host.Paths.RootDirectory) { UseShellExecute = true }); }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception) { StatusText.Text = exception.Message; }
    }

    private async void ClearDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Localize.T("Text0121"), Localize.T("Text0122"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await _host.Diagnostics.ClearAsync(); StatusText.Text = Localize.T("Text0123"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { StatusText.Text = Localize.T("Text0124") + exception.Message; }
    }
}
