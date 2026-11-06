using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Context;
using ArkTerminal.Terminal.Rendering;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Sessions;

public sealed record TerminalContextSource(ConPtySession Session, ContextMetadata Metadata, TerminalTextRange? Selection);

public sealed class TerminalContextCollector(Func<Guid, CancellationToken, Task<TerminalContextSource?>> source) : IContextCollector
{
    public async Task<ContextSnapshot> CollectAsync(ContextCollectionRequest request, CancellationToken cancellationToken)
    {
        var current = await source(request.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(Localize.T("Text0418"));
        var selection = request.PreferSelection || request.SelectionOnly ? current.Selection : null;
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = request.SelectionOnly && selection is null ? new TerminalTextSlice(string.Empty, false, false, false) :
                current.Session.CaptureBoundedText(request.MaxLines, request.MaxBytes, selection);
            if (selection is not null && !raw.Available && !request.SelectionOnly)
                throw new InvalidOperationException(Localize.T("Text0419"));
            var body = ContextText.Prepare(raw.Text, raw.Truncated, request.MaxLines, request.MaxBytes);
            var time = DateTimeOffset.UtcNow;
            return new ContextSnapshot
            {
                SessionId = current.Session.Id, Text = body.Text, Truncated = body.Truncated, Redactions = body.Redactions,
                HasSelection = selection is not null && raw.Available, CapturedAtUtc = time,
                Content = ContextDocument.Create(current.Metadata, body, selection is not null, request.Purpose, time)
            };
        }, cancellationToken).ConfigureAwait(false);
    }
}
