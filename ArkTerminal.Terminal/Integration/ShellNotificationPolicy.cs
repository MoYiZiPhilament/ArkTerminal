using ArkTerminal.Core.Configuration;

namespace ArkTerminal.Terminal.Integration;

public enum ShellNotificationKind { CommandCompleted, LongTask, ReadyForInput }
public sealed class ShellNotificationPolicy
{
    private readonly Dictionary<Guid, (long Command, DateTimeOffset Time)> _last = [];
    private DateTimeOffset _lastGlobal;
    public ShellNotificationKind? Evaluate(Guid session, ShellIntegrationEventArgs signal, bool focused, ShellNotificationSettings settings, DateTimeOffset now)
    {
        lock (_last)
        {
            if (focused || signal.Status.CommandId <= 0 || signal.Status.State == ShellCommandState.Running) return null;
            ShellNotificationKind? kind = signal.Kind == ShellEventKind.Completed && signal.Status.Duration?.TotalSeconds >= settings.LongTaskSeconds && settings.LongTaskEnabled ? ShellNotificationKind.LongTask :
                signal.Kind == ShellEventKind.Completed && settings.CommandCompletedEnabled ? ShellNotificationKind.CommandCompleted :
                signal.Kind == ShellEventKind.Prompt && settings.ReadyForInputEnabled ? ShellNotificationKind.ReadyForInput : null;
            if (kind is null || (_last.TryGetValue(session, out var previous) && (previous.Command == signal.Status.CommandId || now - previous.Time < TimeSpan.FromSeconds(5))) || now - _lastGlobal < TimeSpan.FromSeconds(5)) return null;
            _last[session] = (signal.Status.CommandId, now); _lastGlobal = now;
            return kind;
        }
    }
    public void Remove(Guid session) { lock (_last) _last.Remove(session); }
}
