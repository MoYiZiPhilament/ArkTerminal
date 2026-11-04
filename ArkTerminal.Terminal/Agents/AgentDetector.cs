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
            throw new ArgumentException("Agent 检测限制无效。");
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
                CacheWarning = saved.Success ? null : "Agent 检测结果已更新，但缓存保存失败；原文件保留。";
            }
            else CacheWarning = "Agent 缓存无效、不可读或来自更高版本；已忽略且未覆盖。";
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
        if (!entry.Enabled) { result.Status = AgentDetectionStatus.Unsupported; result.Reason = "JSON 未启用或内容已改变；未查找或运行其声明的程序。"; return result; }
        if (definition is null) { result.Status = AgentDetectionStatus.InvalidConfiguration; result.Reason = "适配器无效。"; return result; }
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
                result.Reason = (previous.Reason ?? "").Replace("（启动轻量校验，未重新执行版本命令）", "", StringComparison.Ordinal) + "（启动轻量校验，未重新执行版本命令）";
                return result;
            }
            var found = FindExecutable(definition, overridePath, _limits.MaxPathDirectories, token);
            result.ResolvedPath = found.Path; result.Source = found.Source;
            if (found.Path is null)
            {
                result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.NotInstalled : AgentDetectionStatus.Unsupported;
                result.Reason = definition.UnsupportedReason ?? (overridePath is null ? "PATH 与有限常见目录未找到候选；未扫描全盘。" : "覆盖路径不存在；未回退到其他程序。");
                return result;
            }
            var metadata = Stamp(found.Path); result.FileLength = metadata.Length; result.LastWriteTimeUtc = metadata.Modified;
            result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.FoundUnverified : AgentDetectionStatus.Unsupported;
            result.Reason = definition.UnsupportedReason ?? "已发现文件；尚未运行版本探测。刷新版本会执行第三方程序。";
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
                if (output.TooLarge) result.Reason = "版本输出超过上限，已结束探测进程；未保存原始输出。";
                else if (output.ExitCode != 0) result.Reason = $"版本命令退出码 {output.ExitCode}；文件已发现但未验证。";
                else
                {
                    var match = Regex.Match(output.Text, @"(?<![\w-])v?\d{1,5}\.\d{1,5}(?:\.\d{1,5})?(?:-[a-zA-Z0-9.]{1,24})?(?![\w-])");
                    if (!match.Success) result.Reason = "版本命令成功，但没有可确认的版本号；交互能力未经本机验收。";
                    else
                    {
                        result.Version = match.Value;
                        result.Status = definition.Capabilities.InteractiveInput ? AgentDetectionStatus.Verified : AgentDetectionStatus.Unsupported;
                        result.Reason = definition.UnsupportedReason ?? "路径及版本命令已验证；交互能力依据适配器资料，未自动运行 Agent 任务。";
                    }
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { result.Status = AgentDetectionStatus.TimedOut; result.Reason = "版本探测超时，已回收探测进程树；可手动重试。"; result.ProbedAtUtc = DateTimeOffset.UtcNow; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException)
        {
            result.Status = result.ResolvedPath is null && overridePath is not null ? AgentDetectionStatus.InvalidConfiguration : AgentDetectionStatus.FoundUnverified;
            result.Reason = $"文件无法验证（错误码 0x{error.HResult:X8}）；请检查路径、执行权限或包装器类型。";
        }
        return result;
    }

    public static (string? Path, string Source) FindExecutable(AgentDefinition definition, string? overridePath, int maxDirectories = 128, CancellationToken token = default)
    {
        if (!string.IsNullOrEmpty(overridePath))
        {
            if (!Path.IsPathFullyQualified(overridePath) || !new[] { ".exe", ".cmd", ".ps1" }.Contains(Path.GetExtension(overridePath), StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("覆盖路径须为 .exe、.cmd 或 .ps1 的绝对路径。");
            try
            {
                return (!File.GetAttributes(overridePath).HasFlag(FileAttributes.Directory) ? Path.GetFullPath(overridePath) : null, "用户覆盖");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return (null, "用户覆盖"); }
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var common = new[] { Path.Combine(roaming, "npm"), Path.Combine(home, ".local", "bin"), Path.Combine(home, ".cargo", "bin"),
            Path.Combine(home, ".kimi-code", "bin"), Path.Combine(home, ".bun", "bin"), Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Path.Combine(local, "Microsoft", "WindowsApps"), Path.Combine(local, "agy", "bin"), Path.Combine(local, "Programs", "opencode", "bin") };
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(maxDirectories).Select(value => (Path: value.Trim('"'), Source: "PATH"));
        foreach (var directory in pathDirectories.Concat(common.Select(value => (Path: value, Source: "常见目录"))).DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(directory.Path) || directory.Path.StartsWith(@"\\", StringComparison.Ordinal)) continue;
            foreach (var candidate in definition.ExecutableCandidates)
            {
                var path = Path.Combine(directory.Path, candidate);
                if (File.Exists(path)) return (Path.GetFullPath(path), directory.Source);
            }
        }
        return (null, "PATH / 常见目录");
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
        ? ValidationResult.Valid() : ValidationResult.FromIssues([new("agents", "检测缓存无效。")]);
}
