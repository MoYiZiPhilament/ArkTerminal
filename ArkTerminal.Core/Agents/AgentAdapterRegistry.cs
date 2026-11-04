using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArkTerminal.Core.Data;
using ArkTerminal.Core.Storage;
using ArkTerminal.Core.Validation;

namespace ArkTerminal.Core.Agents;

public sealed record AgentAdapterEntry(string FileName, AgentDefinition? Definition, string Revision, bool Enabled, string? Error, bool BuiltIn = false);

public sealed class AgentPreferences
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, string> EnabledAdapters { get; set; } = [];
    public Dictionary<string, string> OverridePaths { get; set; } = [];
}

public sealed class AgentDetectionCache
{
    public int SchemaVersion { get; set; } = 1;
    public string SearchRevision { get; set; } = string.Empty;
    public List<AgentDetectionResult> Results { get; set; } = [];
}

public sealed class AgentAdapterRegistry
{
    private readonly IAppDataPaths _paths;
    private readonly AgentDetectionOptions _limits;
    private readonly JsonDocumentStore<AgentPreferences> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AgentPreferences _preferences = new();
    private string? _revision;
    private bool _writable = true;
    public IReadOnlyList<AgentAdapterEntry> Entries { get; private set; } = [];
    public string? Warning { get; private set; }
    public IReadOnlyDictionary<string, string> OverridePaths => _preferences.OverridePaths;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = AgentLimits.MaxJsonDepth, WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public AgentAdapterRegistry(IAppDataPaths paths, AgentDetectionOptions? limits = null)
    {
        _paths = paths; _limits = limits ?? new();
        if (_limits.MaxAdapters < 7 || _limits.MaxAdapters > AgentLimits.MaxAdapters || _limits.MaxAdapterBytes is < 1024 or > AgentLimits.MaxAdapterBytes)
            throw new ArgumentException("适配器总量或文件大小限制无效。");
        _store = new(paths.AgentPreferencesFile, paths.BackupDirectory, new() { MaxDocumentBytes = 65536 }, ValidatePreferences);
    }

    public async Task ReloadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var loaded = await _store.LoadAsync(token).ConfigureAwait(false);
            _writable = loaded.Success || loaded.Status == DocumentLoadStatus.NotFound;
            _preferences = loaded.Value ?? new(); _revision = loaded.Revision ?? string.Empty;
            Warning = _writable ? null : "Agent 偏好文件无效或来自更高版本；仅使用内置适配器，未覆盖文件。";
            Entries = await Task.Run(() => ReadEntries(token), token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private IReadOnlyList<AgentAdapterEntry> ReadEntries(CancellationToken token)
    {
        var entries = BuiltInAgents.Create().Select(definition => new AgentAdapterEntry("内置", definition, Revision(definition), true, null, true)).ToList();
        try
        {
            var files = new DirectoryInfo(_paths.AdaptersDirectory).EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
                .Take(_limits.MaxAdapters - entries.Count + 1).ToArray();
            if (files.Length > _limits.MaxAdapters - entries.Count) Warning = "JSON 适配器超过总计 32 项上限；超出的文件未加载。";
            foreach (var file in files.Take(_limits.MaxAdapters - entries.Count))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new ArgumentException("不加载重解析点文件。");
                    if (file.Length > _limits.MaxAdapterBytes) throw new ArgumentException("JSON 超过 64 KiB 上限。");
                    using var stream = file.OpenRead();
                    var bytes = new byte[_limits.MaxAdapterBytes + 1];
                    var count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                    if (count > _limits.MaxAdapterBytes) throw new ArgumentException("JSON 超过 64 KiB 上限。");
                    var definition = Parse(bytes.AsSpan(0, count));
                    var revision = Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, count)));
                    entries.Add(new(file.Name, definition, revision, _preferences.EnabledAdapters.GetValueOrDefault(file.Name) == revision, null));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                {
                    entries.Add(new(file.Name, null, string.Empty, false, error is ArgumentException ? error.Message : "JSON 损坏、字段未知、深度超限或文件不可读。"));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Warning = "适配器目录不可读；使用内置适配器。"; }
        foreach (var group in entries.Where(entry => entry.Definition is not null).GroupBy(entry => entry.Definition!.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (var entry in group.Where(entry => !entry.BuiltIn))
                entries[entries.IndexOf(entry)] = entry with { Enabled = false, Error = "适配器 ID 冲突；请修正 JSON。" };
        }
        return entries;
    }

    public static AgentDefinition Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > AgentLimits.MaxAdapterBytes) throw new ArgumentException("JSON 超过 64 KiB 上限。");
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) bytes = bytes[3..];
        // Reject duplicate keys as well as unknown fields; last-value-wins is unsafe for a review screen.
        using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = AgentLimits.MaxJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("JSON 根必须是对象。");
        foreach (var required in new[] { "schemaVersion", "id", "displayName", "executableCandidates", "launchTemplate", "variables", "fields", "capabilities", "statusSignals", "evidence" })
            if (!document.RootElement.TryGetProperty(required, out _)) throw new ArgumentException("JSON 缺少必需字段：" + required);
        void Unique(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                { if (!names.Add(property.Name)) throw new ArgumentException("JSON 包含重复字段。"); Unique(property.Value); }
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Unique(item);
        }
        Unique(document.RootElement);
        var definition = JsonSerializer.Deserialize<AgentDefinition>(bytes, JsonOptions) ?? throw new ArgumentException("JSON 根必须是适配器对象。");
        ValidateDefinition(definition);
        return definition;
    }

    public static void ValidateDefinition(AgentDefinition definition)
    {
        if (definition.SchemaVersion != 1 || !Id(definition.Id) || string.IsNullOrWhiteSpace(definition.DisplayName) || definition.DisplayName.Length > 128 || definition.DisplayName.Any(char.IsControl))
            throw new ArgumentException("schemaVersion 仅支持 1；ID 须为稳定标识，名称最多 128 字符。");
        if (definition.ExecutableCandidates is null || definition.ExecutableCandidates.Count is < 1 or > 8 || definition.ExecutableCandidates.Any(name =>
                string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.Any(char.IsControl) ||
                !new[] { ".exe", ".cmd", ".ps1" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("可执行候选须为 1—8 个 .exe / .cmd / .ps1 文件名，不含目录。");
        if (definition.OverridePath is { } path && (!Path.IsPathFullyQualified(path) || path.Length > 2048 || path.Any(char.IsControl)))
            throw new ArgumentException("覆盖路径须为完整本地文件路径。");
        if (definition.Variables is null || definition.Variables.Count > 2 || definition.Variables.Distinct().Count() != definition.Variables.Count ||
            definition.Variables.Any(value => value is not ("model" or "workingDirectory"))) throw new ArgumentException("变量仅允许 model 与 workingDirectory。");
        void Template(List<string>? values, bool variables = true)
        {
            if (values is null || values.Count > 32 || values.Any(value => value is null || value.Length > 2048 || value.Any(char.IsControl) || AgentRecipeRules.ContainsSecret(value)))
                throw new ArgumentException("参数模板超限、包含凭据或控制字符。");
            foreach (var value in values)
                if ((value.Contains("{{") || value.Contains("}}")) && (!variables || !definition.Variables.Any(variable => value == "{{" + variable + "}}")))
                    throw new ArgumentException("未知变量或内嵌模板；变量必须占据完整参数，且不递归展开。");
        }
        if (definition.VersionProbe is { } probe)
        {
            Template(probe.Arguments, false);
            if (probe.Arguments.Count is < 1 or > 8 || probe.TimeoutMilliseconds is < 100 or > 3000 || probe.MaxOutputBytes is < 1024 or > 65536)
                throw new ArgumentException("版本探测须有参数，超时为 100—3000 ms，输出为 1—64 KiB。");
        }
        Template(definition.LaunchTemplate);
        if (definition.LaunchTemplate.Contains("{{model}}")) throw new ArgumentException("model 变量仅用于 fields.model 模板。");
        if (definition.Fields is null || definition.Fields.PermissionModes is null || definition.Fields.Arguments is null || definition.Capabilities is null)
            throw new ArgumentException("fields 与 capabilities 不能为 null。");
        Template(definition.Fields.Model);
        if (definition.Fields.PermissionModes.Count > 16 || definition.Fields.Arguments.Count > 16) throw new ArgumentException("字段白名单超限。");
        foreach (var (name, values) in definition.Fields.PermissionModes)
        { if (!Id(name)) throw new ArgumentException("权限模式 ID 无效。"); Template(values); if (values.Contains("{{model}}")) throw new ArgumentException("model 变量仅用于 fields.model 模板。"); }
        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in definition.Fields.Arguments)
        {
            if (option is null || !Regex.IsMatch(option.Flag, @"\A--?[A-Za-z][A-Za-z0-9-]{0,63}\z") || !flags.Add(option.Flag) ||
                Regex.IsMatch(option.Flag, "(?i)key|token|password|secret|auth") || option.AllowedValues is null || option.AllowedValues.Count > 32)
                throw new ArgumentException("附加参数开关无效、重复或属于凭据字段。");
            Template(option.AllowedValues, false);
        }
        if (definition.Capabilities.ProgrammaticHandoff || definition.StatusSignals is null || definition.StatusSignals.Count > 1 || definition.StatusSignals.Any(signal => signal != "process"))
            throw new ArgumentException("当前仅支持 process 状态信号与终端文本输入，不支持程序化交接或自定义状态钩子。");
        if (definition.Evidence is null || definition.Evidence.Length > 2048 || definition.Evidence.Any(char.IsControl) || definition.UnsupportedReason?.Length > 2048)
            throw new ArgumentException("核实依据 / 不支持原因超限。");
    }

    public async Task SetEnabledAsync(AgentAdapterEntry entry, bool enabled, CancellationToken token)
    {
        if (entry.BuiltIn || entry.Error is not null || entry.Definition is null) throw new ArgumentException("只能启用有效且无 ID 冲突的 JSON 适配器。");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await Task.Run(() => ReadEntries(token).FirstOrDefault(item => item.FileName == entry.FileName), token).ConfigureAwait(false);
            if (current?.Revision != entry.Revision || current.Error is not null) throw new ArgumentException("JSON 已变更；请重新加载并审核。");
            var preferences = ClonePreferences();
            if (enabled) preferences.EnabledAdapters[entry.FileName] = entry.Revision;
            else preferences.EnabledAdapters.Remove(entry.FileName);
            await SaveAsync(preferences, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        await ReloadAsync(token).ConfigureAwait(false);
    }

    public async Task SetOverrideAsync(string id, string? path, CancellationToken token)
    {
        if (!Entries.Any(entry => entry.Definition?.Id == id && entry.Error is null)) throw new ArgumentException("适配器不可用。");
        if (!string.IsNullOrEmpty(path) && (!Path.IsPathFullyQualified(path) || path.Length > 2048 || path.Any(char.IsControl)))
            throw new ArgumentException("覆盖路径须为完整文件路径；留空恢复自动查找。");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var preferences = ClonePreferences();
            if (string.IsNullOrEmpty(path)) preferences.OverridePaths.Remove(id); else preferences.OverridePaths[id] = path;
            await SaveAsync(preferences, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private AgentPreferences ClonePreferences() => new() { EnabledAdapters = new(_preferences.EnabledAdapters), OverridePaths = new(_preferences.OverridePaths) };
    private async Task SaveAsync(AgentPreferences preferences, CancellationToken token)
    {
        if (!_writable) throw new InvalidOperationException("Agent 偏好文件受保护；请先修正或移出无效 / 高版本文件。");
        var saved = await _store.SaveAsync(preferences, _revision, cancellationToken: token).ConfigureAwait(false);
        if (!saved.Success) throw new IOException(saved.Error);
        _preferences = preferences; _revision = saved.Revision;
    }
    public static string Revision(AgentDefinition definition) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definition, JsonOptions)));
    private static bool Id(string? id) => id is not null && Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z");
    private static ValidationResult ValidatePreferences(AgentPreferences value) =>
        value?.SchemaVersion == 1 && value.EnabledAdapters is not null && value.OverridePaths is not null && value.EnabledAdapters.Count <= 32 && value.OverridePaths.Count <= 32 &&
        value.EnabledAdapters.All(pair => Path.GetFileName(pair.Key) == pair.Key && pair.Value?.Length == 64) &&
        value.OverridePaths.All(pair => Id(pair.Key) && pair.Value is not null && Path.IsPathFullyQualified(pair.Value) && pair.Value.Length <= 2048 && !pair.Value.Any(char.IsControl))
            ? ValidationResult.Valid() : ValidationResult.FromIssues([new("agents", "Agent 偏好无效。")]);
}
