using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Data;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Storage;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Terminal.Agents;

public sealed class AgentDetector : IAgentDetector
{
    private readonly AgentAdapterRegistry _registry;
    private readonly AgentDetectionOptions _limits;
    private readonly JsonDocumentStore<AgentDetectionCache> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public IReadOnlyList<AgentDetectionResult> Results { get; private set; } = [];
    public string? CacheWarning { get; private set; }

    public AgentDetector(IAppDataPaths paths, AgentAdapterRegistry registry, AgentDetectionOptions? limits = null)
    {
        _registry = registry; _limits = limits ?? new();
        if (_limits.MaxConcurrency is < 1 or > 8 || _limits.ProbeTimeoutMilliseconds is < 100 or > 3000 || _limits.MaxProbeOutputBytes is < 1024 or > 65536 || _limits.MaxPathDirectories is < 1 or > 128 || _limits.CacheLifetime <= TimeSpan.Zero)
            throw new ArgumentException(Localize.T("Text0451"));
        _store = new(paths.AgentDetectionCacheFile, paths.BackupDirectory, new() { MaxDocumentBytes = 131072 }, ValidateCache);
    }

    public Task<IReadOnlyList<AgentDetectionResult>> DetectAsync(CancellationToken cancellationToken) => DetectAsync(false, cancellationToken);

    public async Task<IReadOnlyList<AgentDetectionResult>> DetectAsync(bool probeVersions, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            var cached = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var searchRevision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("PATH") ?? "")));
            var overrides = new Dictionary<string, string>(_registry.OverridePaths);
            using var concurrency = new SemaphoreSlim(_limits.MaxConcurrency);
            var tasks = _registry.Entries.Select(async entry =>
            {
                await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var previous = cached.Value?.SearchRevision == searchRevision
                        ? cached.Value.Results.FirstOrDefault(result => result.DefinitionId == entry.Definition?.Id) : null;
                    return await Task.Run(() => DetectOneAsync(entry, overrides, previous, probeVersions, cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                finally { concurrency.Release(); }
            }).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var cache = new AgentDetectionCache { SearchRevision = searchRevision, Results = results.Where(result => result.Status != AgentDetectionStatus.InvalidConfiguration).ToList() };
            if (cached.Success || cached.Status == DocumentLoadStatus.NotFound)
            {
                var saved = await _store.SaveAsync(cache, cached.Revision ?? string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
                CacheWarning = saved.Success ? null : Localize.T("Text0452");
            }
            else CacheWarning = Localize.T("Text0453");
            Results = results;
            return results;
        }
        finally { _gate.Release(); }
    }

    private async Task<AgentDetectionResult> DetectOneAsync(AgentAdapterEntry entry, IReadOnlyDictionary<string, string> overrides,
        AgentDetectionResult? previous, bool probe, CancellationToken token)
    {
        var definition = entry.Definition;
        var result = new AgentDetectionResult { DefinitionId = definition?.Id ?? entry.FileName, DetectedAtUtc = DateTimeOffset.UtcNow, DefinitionRevision = entry.Revision };
        if (entry.Error is not null) { result.Status = AgentDetectionStatus.InvalidConfiguration; result.Reason = entry.Error; return result; }
        if (!entry.Enabled) { result.Status = AgentDetectionStatus.Unsupported; result.Reason = Localize.T("Text0454"); return result; }
        if (definition is null) { result.Status = AgentDetectionStatus.InvalidConfiguration; result.Reason = Localize.T("Text0455"); return result; }
        var overridePath = overrides.GetValueOrDefault(definition.Id) ?? definition.OverridePath;
        result.DefinitionRevision += "|" + overridePath;
        try
        {
            if (!probe && previous is not null && previous.DefinitionRevision == result.DefinitionRevision &&
                previous.ResolvedPath is { } cachedPath && previous.DetectedAtUtc > DateTimeOffset.UtcNow - _limits.CacheLifetime &&
                (previous.ProbedAtUtc is null || previous.ProbedAtUtc > DateTimeOffset.UtcNow - _limits.CacheLifetime) &&
                MatchesMetadata(cachedPath, previous))
            {
                result.ResolvedPath = cachedPath; result.Source = previous.Source; result.Version = previous.Version; result.ProbedAtUtc = previous.ProbedAtUtc;
                result.FileLength = previous.FileLength; result.LastWriteTimeUtc = previous.LastWriteTimeUtc; result.Status = previous.Status;
                result.Reason = (previous.Reason ?? "").Replace(Localize.T("Text0456"), "", StringComparison.Ordinal) + Localize.T("Text0456");
                return result;
            }
            var found = FindExecutable(definition, overridePath, _limits.MaxPathDirectories, token);
            result.ResolvedPath = found.Path; result.Source = found.Source;
            if (found.Path is null)
            {
                result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.NotInstalled : AgentDetectionStatus.Unsupported;
                result.Reason = definition.UnsupportedReason ?? (overridePath is null ? Localize.T("Text0457") : Localize.T("Text0458"));
                return result;
            }
            var metadata = Stamp(found.Path); result.FileLength = metadata.Length; result.LastWriteTimeUtc = metadata.Modified;
            result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.FoundUnverified : AgentDetectionStatus.Unsupported;
            result.Reason = definition.UnsupportedReason ?? Localize.T("Text0459");
            if (!probe || definition.VersionProbe is not { } version) return result;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(Math.Min(_limits.ProbeTimeoutMilliseconds, version.TimeoutMilliseconds));
            try
            {
                var executable = found.Path;
                IReadOnlyList<string> arguments = version.Arguments;
                if (!Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    arguments = AgentPowerShell.BuildShellArguments(executable, arguments);
                    executable = AgentPowerShell.ResolveBackend(AgentShellBackend.PowerShell7).ExecutablePath;
                }
                var output = await AgentVersionProcess.RunAsync(executable, arguments, Math.Min(version.MaxOutputBytes, _limits.MaxProbeOutputBytes), deadline.Token).ConfigureAwait(false);
                result.ProbedAtUtc = DateTimeOffset.UtcNow;
                if (output.TooLarge) result.Reason = Localize.T("Text0460");
                else if (output.ExitCode != 0) result.Reason = Localize.F("Text0461", output.ExitCode);
                else
                {
                    var match = Regex.Match(output.Text, @"(?<![\w-])v?\d{1,5}\.\d{1,5}(?:\.\d{1,5})?(?:-[a-zA-Z0-9.]{1,24})?(?![\w-])");
                    if (!match.Success) result.Reason = Localize.T("Text0462");
                    else
                    {
                        result.Version = match.Value;
                        result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.Verified : AgentDetectionStatus.Unsupported;
                        result.Reason = definition.UnsupportedReason ?? Localize.T("Text0463");
                    }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { result.Status = AgentDetectionStatus.TimedOut; result.Reason = Localize.T("Text0464"); result.ProbedAtUtc = DateTimeOffset.UtcNow; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException)
        {
            result.Status = result.ResolvedPath is null && overridePath is not null ? AgentDetectionStatus.InvalidConfiguration : AgentDetectionStatus.FoundUnverified;
            result.Reason = Localize.F("Text0465", error.HResult);
        }
        return result;
    }

    public static (string? Path, string Source) FindExecutable(AgentDefinition definition, string? overridePath, int maxDirectories = 128, CancellationToken token = default)
    {
        if (!string.IsNullOrEmpty(overridePath))
        {
            if (!Path.IsPathFullyQualified(overridePath) || !new[] { ".exe", ".cmd", ".ps1" }.Contains(Path.GetExtension(overridePath), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException(Localize.T("Text0466"));
            try
            {
                return (!File.GetAttributes(overridePath).HasFlag(FileAttributes.Directory) ? Path.GetFullPath(overridePath) : null, Localize.T("Text0467"));
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return (null, Localize.T("Text0467")); }
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var common = new[] { Path.Combine(roaming, "npm"), Path.Combine(home, ".local", "bin"), Path.Combine(home, ".cargo", "bin"),
            Path.Combine(home, ".kimi-code", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Path.Combine(local, "Microsoft", "WindowsApps"), Path.Combine(local, "agy", "bin"), Path.Combine(local, "Programs", "opencode", "bin") };
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(maxDirectories).Select(value => (Path: value.Trim('"'), Source: "PATH"));
        foreach (var directory in pathDirectories.Concat(common.Select(value => (Path: value, Source: Localize.T("Text0468")))).DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(directory.Path) || directory.Path.StartsWith(@"\\", StringComparison.Ordinal)) continue;
            foreach (var candidate in definition.ExecutableCandidates)
            {
                var path = Path.Combine(directory.Path, candidate);
                if (File.Exists(path)) return (Path.GetFullPath(path), directory.Source);
            }
        }
        return (null, Localize.T("Text0469"));
    }

    private static (long Length, DateTime Modified) Stamp(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException();
        if (file.LinkTarget is not null && file.ResolveLinkTarget(true) is FileInfo target) file = target;
        return (file.Length, file.LastWriteTimeUtc);
    }
    private static bool MatchesMetadata(string path, AgentDetectionResult previous)
    {
        try { var stamp = Stamp(path); return stamp.Length == previous.FileLength && stamp.Modified == previous.LastWriteTimeUtc; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }
    private static ValidationResult ValidateCache(AgentDetectionCache value) => value?.SchemaVersion == 1 && value.Results is not null && value.Results.Count <= 32 &&
        value.Results.All(result => result is not null && !string.IsNullOrEmpty(result.DefinitionId) && result.DefinitionId.Length <= 128 && Enum.IsDefined(result.Status) && result.ResolvedPath?.Length is not > 2048 && result.Reason?.Length is not > 2048 && result.Version?.Length is not > 64)
        ? ValidationResult.Valid() : ValidationResult.FromIssues([new("agents", Localize.T("Text0470"))]);
}
