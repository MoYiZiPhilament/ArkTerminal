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
                        ShellNotificationKind.LongTask => Localize.T("Text0145"),
                        ShellNotificationKind.CommandCompleted => Localize.T("Text0146"),
                        _ => Localize.T("Text0147")
                    };
                    if (_shellNotifications?.Show(message) == false) StatusText.Text = Localize.T("Text0148");
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
            var state = shell.State switch { ShellCommandState.Ready => Localize.T("Text0149"), ShellCommandState.Running => Localize.T("Text0150"), _ => Localize.T("Text0151") };
            var code = shell.ExitCode is { } exit ? Localize.F("Text0152", exit) : shell.Succeeded is { } success ? success ? Localize.T("Text0153") : Localize.T("Text0154") : string.Empty;
            TerminalStatusText.Text = $"{_tabs.ActiveTab.StatusText} · {(_tabs.ActiveTab.Launch?.Request.RunAsAdministrator == true ? Localize.T("Text0835") : string.Empty)}{state}{code} · {shell.WorkingDirectory ?? Localize.T("Text0836")}";
            TerminalStatusText.ToolTip = shell.Detail + Localize.T("Text0155");
        }
        if (_tabs.ActiveTab.Launch?.Request.AgentRecipe is { } agent)
        {
            TerminalStatusText.Text = Localize.F("Text0156", _tabs.ActiveTab.StatusText, agent.AgentId, _tabs.ActiveTab.AgentBackendDescription);
            TerminalStatusText.ToolTip = Localize.T("Text0157");
        }
        CommandDurationText.Text = shell?.Duration is { } duration ? Localize.F("Text0158", duration.TotalSeconds) : Localize.T("Text0137");
        TranscriptStatusText.Text = transcript?.Recording == true ? transcript.Truncated ? Localize.T("Text0159") : Localize.T("Text0160") :
            transcript?.Message.Contains(Localize.T("Text0161"), StringComparison.Ordinal) == true ? Localize.T("Text0162") : transcript?.Truncated == true ? Localize.T("Text0163") : string.Empty;
        TranscriptStatusText.ToolTip = transcript?.Message;
        ToggleTranscriptButton.IsEnabled = session?.State == SessionState.Running && !_recordingBusy;
        ToggleTranscriptButton.Content = transcript?.Recording == true ? Localize.T("Text0164") : Localize.T("Text0165");
        HistoryEnabledInput.IsChecked = _host.CommandHistory.Enabled;
        TranscriptionEnabledInput.IsChecked = transcript?.Recording == true;
        TranscriptionEnabledInput.IsEnabled = session?.State == SessionState.Running && !_recordingBusy;
        var policy = _host.FormalSettings?.Transcription ?? new();
        RecordingStatusText.Text = Localize.RefreshText(_host.CommandHistory.Status) + "\n" + (transcript?.Message is { } message ? Localize.RefreshText(message) : Localize.T("Text0166")) +
            Localize.T("Text0167") + (_host.FormalSettings?.History.MaxEntries ?? 1000) + Localize.T("Text0168") + (_host.FormalSettings?.History.MaxSizeBytes ?? 1048576) +
            Localize.F("Text0169", policy.MaxFileSizeBytes, policy.MaxTotalSizeBytes, policy.RetentionDays);
    }

    private async Task<SessionLaunchRequest?> ConfirmElevatedLaunchAsync(SessionLaunchRequest request)
    {
        var approved = await Task.Run(() => LaunchPathInspector.Resolve(request));
        var text = new StringBuilder(Localize.T("Text0170"))
            .Append(Localize.T("Text0171")).Append(approved.ExecutablePath).Append(Localize.T("Text0172")).Append(approved.WorkingDirectory)
            .Append(Localize.T("Text0173"));
        foreach (var argument in approved.Arguments) text.Append(System.Text.Json.JsonSerializer.Serialize(argument)).Append('\n');
        text.Append(Localize.T("Text0174"));
        text.Append(request.Environment.Count).Append(Localize.T("Text0175"));
        text.Append(Localize.T("Text0176")).Append(System.Text.Json.JsonSerializer.Serialize(approved.PostLaunchInput));
        if (text.Length > 12000) throw new ArgumentException(Localize.T("Text0177"));
        return MessageBox.Show(this, text.ToString(), Localize.T("Text0178"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes ? approved : null;
    }

    private async void OpenTerminalLink(TerminalLink link)
    {
        var action = link.IsWeb ? Localize.T("Text0179") : Localize.T("Text0180");
        if (MessageBox.Show(this, Localize.F("Text0181", link.DisplayText, link.Target, action), Localize.T("Text0182"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        { StatusText.Text = Localize.F("Text0183", link.Target, error.Message); }
    }

    private async void HistoryEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingBusy) return;
        var enable = HistoryEnabledInput.IsChecked == true;
        if (enable && MessageBox.Show(this, Localize.T("Text0184"), Localize.T("Text0185"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        if (enable && MessageBox.Show(this, Localize.F("Text0186", policy.MaxFileSizeBytes, policy.MaxTotalSizeBytes, policy.RetentionDays), Localize.T("Text0187"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        { UpdateShellDisplay(); return; }
        _recordingBusy = true;
        try
        {
            if (enable) await _host.Transcripts.StartAsync(session.Id, policy);
            else await _host.Transcripts.StopAsync(session.Id);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = Localize.T("Text0188") + error.Message; }
        finally { _recordingBusy = false; UpdateShellDisplay(); }
    }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Localize.T("Text0189"), Localize.T("Text0190"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await _host.CommandHistory.ClearAsync(); StatusText.Text = Localize.T("Text0191"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = Localize.T("Text0192") + error.Message; }
    }
    private async void ClearTranscripts_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Localize.T("Text0193"), Localize.T("Text0194"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { await _host.Transcripts.ClearAsync(); StatusText.Text = Localize.T("Text0195"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { StatusText.Text = Localize.T("Text0196") + error.Message; }
    }
    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        var query = new TextBox { Margin = new(0, 0, 0, 8) };
        var results = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var search = new Button { Content = Localize.T("Text0197"), Margin = new(0, 0, 0, 8) };
        var panel = new DockPanel { Margin = new(16) };
        DockPanel.SetDock(query, Dock.Top); DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(query); panel.Children.Add(search); panel.Children.Add(results);
        var window = new Window { Owner = this, Title = Localize.T("Text0198"), Width = 780, Height = 580, Content = panel };
        window.SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        window.SetResourceReference(ForegroundProperty, "Theme.PrimaryText");
        async Task Load()
        {
            search.IsEnabled = false;
            try
            {
                var entries = await _host.CommandHistory.ReadAsync(query.Text);
                results.Text = string.Join("\n\n", entries.Take(100).Select(item => Localize.F("Text0199", Localize.ToConfiguredTime(item.TimeUtc), item.Shell, item.ExitCode?.ToString() ?? Localize.T("Text0056"), item.DurationMilliseconds / 1000, item.WorkingDirectory, item.Command)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { results.Text = Localize.T("Text0200") + error.Message; }
            finally { search.IsEnabled = true; }
        }
        search.Click += async (_, _) => await Load();
        window.Loaded += async (_, _) => await Load();
        window.Show();
    }
}
