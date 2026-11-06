using System.Text;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Core.Context;

public sealed record ContextMetadata(Guid SessionId, string ProfileId, string ProfileName, string Kind,
    string WorkingDirectory, string DirectorySource, string ProcessName, string State);

public static class ContextDocument
{
    public static string Create(ContextMetadata source, ContextTextResult body, bool selection, string purpose, DateTimeOffset time)
    {
        return Localize.F("Text0630", ContextText.SafeField(purpose), time) +
            Localize.F("Text0631", source.SessionId, ContextText.SafeField(source.ProfileName), ContextText.SafeField(source.ProfileId)) +
            Localize.F("Text0632", source.Kind, ContextText.SafeField(source.State)) +
            (source.Kind == "Agent" ? Localize.T("Text0633") : string.Empty) +
            Localize.F("Text0634", ContextText.SafeField(source.WorkingDirectory, 4096), source.DirectorySource) +
            Localize.F("Text0635", ContextText.SafeField(source.ProcessName)) +
            Localize.F("Text0636", (selection ? Localize.T("Text0849") : Localize.T("Text0850")), ContextLimits.MaxLines, ContextLimits.MaxBytes) +
            Localize.F("Text0637", (body.Truncated ? Localize.T("Text0851") : Localize.T("Text0852")), body.Redactions) +
            Localize.T("Text0638") +
            "    " + body.Text.Replace("\n", "\n    ", StringComparison.Ordinal) + Localize.T("Text0639");
    }

    public static string RiskReminder(string command)
    {
        var risky = new[] { "remove-item", "rm ", "del ", "rmdir", "format", "diskpart", "clear-disk", "git reset", "git clean", "shutdown", "invoke-expression", "iex ", "curl", "wget", "set-executionpolicy" };
        return risky.Any(word => command.Contains(word, StringComparison.OrdinalIgnoreCase))
            ? Localize.T("Text0640")
            : Localize.T("Text0641");
    }
}

public sealed class ContextSnapshotExporter(IAppDataPaths paths)
{
    public string DirectoryPath => paths.SnapshotsDirectory;

    public async Task SaveAsync(string finalPreview, string destination, CancellationToken token)
    {
        ContextText.ValidatePreview(finalPreview);
        var bytes = new UTF8Encoding(false).GetBytes(finalPreview);
        await Task.Run(async () =>
        {
            var fullPath = Path.GetFullPath(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, fullPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, token).ConfigureAwait(false);
    }
}
