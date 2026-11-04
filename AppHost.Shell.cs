using ArkTerminal.Core.Recording;
using ArkTerminal.Terminal.Integration;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal;

public sealed partial class AppHost
{
    public CommandHistoryService CommandHistory { get; private set; } = null!;
    public TranscriptService Transcripts { get; private set; } = null!;
    private Task? _recordingMaintenance;
    private readonly HashSet<Task> _recordingStops = [];

    private void InitializeShellData()
    {
        CommandHistory = new(Paths);
        Transcripts = new(Paths);
        _sessionManager.SessionCreated += session =>
        {
            long historyConsent = -1;
            long commandId = 0;
            session.TextReceived += text => Transcripts.TryAppend(session.Id, text);
            session.ShellIntegrationChanged += (_, args) =>
            {
                if (args.Kind == ShellEventKind.Started)
                { historyConsent = CommandHistory.ConsentVersion; commandId = args.Status.CommandId; }
                else if (args.Kind == ShellEventKind.Completed && args.Status.CommandId == commandId && args.Status.Command is { } command && args.Status.Duration is { } duration)
                    CommandHistory.TryRecord(historyConsent, new(DateTimeOffset.UtcNow, args.Status.Shell, command, args.Status.WorkingDirectory,
                        args.Status.ExitCode, args.Status.Succeeded, duration.TotalMilliseconds));
            };
            session.StateChanged += (_, args) =>
            {
                if (args.State == SessionState.Disposed) TrackRecordingStop(Transcripts.ForgetAsync(session.Id));
                else if (args.State is SessionState.Exited or SessionState.Failed)
                    TrackRecordingStop(Transcripts.StopAsync(session.Id));
            };
        };
    }

    private void TrackRecordingStop(Task operation)
    {
        lock (_recordingStops) _recordingStops.Add(operation);
        _ = operation.ContinueWith(completed =>
        {
            if (completed.IsFaulted) { _ = completed.Exception; Diagnostics.Record(Core.Diagnostics.DiagnosticComponent.Terminal, Core.Diagnostics.DiagnosticEventKind.WriteFailed); }
            lock (_recordingStops) _recordingStops.Remove(completed);
        }, TaskScheduler.Default);
    }
    private async Task WaitForRecordingStopsAsync()
    {
        Task[] pending;
        lock (_recordingStops) pending = _recordingStops.ToArray();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        catch (Exception) { /* Stop failures are observed and recorded without terminal text above. */ }
    }

    private void StartRecordingMaintenance()
    {
        if (FormalSettings is null) return;
        _recordingMaintenance = Task.Run(async () =>
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    try { if (FormalSettings is { } settings) await Transcripts.MaintainAsync(settings.Transcription.DeepClone()).ConfigureAwait(false); }
                    catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
                    { Diagnostics.Record(Core.Diagnostics.DiagnosticComponent.Terminal, Core.Diagnostics.DiagnosticEventKind.CleanupFailed); }
                    await Task.Delay(RecordingLimits.MaintenanceInterval, _lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
}
