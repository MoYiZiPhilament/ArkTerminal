using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Diagnostics;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Sessions.Windows;
using ArkTerminal.Visuals;

namespace ArkTerminal.Controls;

public sealed class ProfileEditorControl : UserControl, IDisposable
{
    private readonly AppHost _host;
    private readonly ListBox _list = new() { Name = "ProfileList", DisplayMemberPath = "Name", SelectedValuePath = "Id", Height = 130 };
    private readonly StackPanel _form = new();
    private readonly Dictionary<string, TextBox> _text = [];
    private readonly Dictionary<string, TextBlock> _errors = [];
    private readonly Dictionary<string, CheckBox> _checks = [];
    private readonly ArgumentListEditor _arguments = new();
    private readonly ArgumentListEditor _agentArguments = new();
    private readonly ComboBox _backend = new() { ItemsSource = Enum.GetValues<AgentShellBackend>() };
    private readonly TextBlock _path = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _pathCancellation;
    private string? _selectedId;
    private bool _loading;
    private int _pathVersion;

    public event Action<string>? StatusChanged;
    public event Action? CatalogSaved;
    public event Action? PreviewChanged;
    public TerminalProfile? PreviewProfile { get; private set; }
    public Rendering.DirectWriteTerminalControl FontPreview { get; } = new() { Height = 110, IsHitTestVisible = false };
    public string? SelectedProfileId => _selectedId;
    private ProfileCatalog Catalog => _host.ProfilesDraft?.Catalog ?? _host.Catalog!;

    public ProfileEditorControl(AppHost host)
    {
        _host = host;
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Profile", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "修改对下次新建 / 重启生效，需单独保存 Profile。", Margin = new(0, 6, 0, 8) });
        root.Children.Add(_list);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 12) };
        AddButton(actions, "新建", () => Add(false));
        AddButton(actions, "复制", () => Add(true));
        AddButton(actions, "删除", Delete);
        root.Children.Add(actions);
        AddText("id", "稳定 ID").IsReadOnly = true;
        AddText("name", "名称");
        AddText("icon", "图标（文字 / Unicode 符号，最多 64 字符）");
        AddText("executablePath", "可执行文件 / PATH 名称");
        AddLabel("Shell 启动参数（逐项填写）", _arguments);
        AddText("postLaunchInput", "启动后原样输入：发送一次，不补 Enter；末尾换行决定最后一行是否执行。", true);
        AddText("workingDirectory", "工作目录（支持 {{home}}）");
        AddText("environment", "环境变量（每行 KEY=VALUE）", true);
        _form.Children.Add(_path);
        AddCheck("terminal.useGlobalFont", "继承全局终端字体和字号（覆盖值保留）");
        AddText("terminal.fontFamily", "覆盖字体");
        AddText("terminal.fontSize", "覆盖字号（6—96）");
        _form.Children.Add(new RoundedBorder { CornerRadius = new(10), Child = FontPreview });
        AddText("terminal.scrollbackLines", "回滚行数（运行时最多 100000 行）");
        AddCheck("administrator", "管理员启动（每次需 Windows UAC）");
        AddCheck("shellIntegration", "Shell 集成（仅当前进程）");
        AddCheck("agent", "使用 Agent 配方");
        AddText("agentRecipe.agentId", "Agent ID（从新建页 Agent 区生成）");
        AddText("agentRecipe.model", "模型标识符");
        AddText("agentRecipe.permissionMode", "Agent 权限参数（不绕过 OS 或 CLI 授权）");
        AddText("agentRecipe.workingDirectory", "Agent 目录（允许 {{home}}）");
        AddLabel("Agent Shell 后端", _backend);
        AddLabel("Agent 附加参数：仅允许适配器白名单，不保存凭据或初始任务。", _agentArguments);
        _form.Children.Add(_summary);
        root.Children.Add(_form);
        Content = root;
        _arguments.Changed += Changed;
        _agentArguments.Changed += Changed;
        _backend.SelectionChanged += (_, _) => Changed();
        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _selectedId = _list.SelectedValue as string;
            LoadSelected(true);
        };
        Reload();
    }

    private TextBox AddText(string key, string label, bool multiline = false)
    {
        var input = new TextBox { Padding = new(6, 4, 6, 4), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden, MinHeight = multiline ? 72 : 26, MaxHeight = multiline ? 180 : double.PositiveInfinity };
        _text[key] = input;
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        System.Windows.Automation.AutomationProperties.SetAutomationId(input, "ProfileField_" + key.Replace('.', '_'));
        AddLabel(label, input);
        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new(0, 2, 0, 6) };
        _errors[key] = error;
        _form.Children.Add(error);
        if (key != "id") input.TextChanged += (_, _) => Changed();
        return input;
    }

    private void AddLabel(string label, UIElement input)
    {
        _form.Children.Add(new TextBlock { Text = label, Margin = new(0, 8, 0, 4) });
        _form.Children.Add(input);
    }

    private void AddCheck(string key, string label)
    {
        var check = new CheckBox { Content = new TextBlock { Text = label }, Margin = new(0, 8, 0, 4) };
        _checks[key] = check;
        check.Checked += (_, _) => Changed();
        check.Unchecked += (_, _) => Changed();
        _form.Children.Add(check);
    }

    private static void AddButton(Panel target, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        target.Children.Add(button);
    }

    public void Reload()
    {
        _loading = true;
        _selectedId = _host.ProfilesDraft?.SelectedProfileId ?? _selectedId;
        _list.ItemsSource = _host.Catalog is null ? null : Catalog.Profiles;
        _list.SelectedValue = _selectedId;
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
        _selectedId = _list.SelectedValue as string;
        _loading = false;
        _form.IsEnabled = _host.CanEdit;
        LoadSelected();
    }

    public void SelectProfile(string id)
    {
        _selectedId = id;
        Reload();
    }

    private Dictionary<string, string> ValuesFrom(TerminalProfile profile) => new()
    {
        ["name"] = profile.Name, ["icon"] = profile.Icon, ["executablePath"] = profile.ExecutablePath,
        ["arguments"] = JsonSerializer.Serialize(profile.Arguments), ["postLaunchInput"] = profile.PostLaunchInput,
        ["workingDirectory"] = profile.WorkingDirectory, ["environment"] = string.Join("\r\n", profile.Environment.Select(pair => pair.Key + "=" + pair.Value)),
        ["terminal.useGlobalFont"] = profile.Terminal.UseGlobalFont.ToString(), ["terminal.fontFamily"] = profile.Terminal.FontFamily,
        ["terminal.fontSize"] = profile.Terminal.FontSize.ToString(CultureInfo.InvariantCulture), ["terminal.scrollbackLines"] = profile.Terminal.ScrollbackLines.ToString(CultureInfo.InvariantCulture),
        ["administrator"] = profile.RunAsAdministrator.ToString(), ["shellIntegration"] = (profile.ShellIntegration == ShellIntegrationMode.Automatic).ToString(),
        ["agent"] = (profile.AgentRecipe is not null).ToString(), ["agentRecipe.agentId"] = profile.AgentRecipe?.AgentId ?? string.Empty,
        ["agentRecipe.model"] = profile.AgentRecipe?.Model ?? string.Empty, ["agentRecipe.permissionMode"] = profile.AgentRecipe?.PermissionMode ?? string.Empty,
        ["agentRecipe.workingDirectory"] = profile.AgentRecipe?.WorkingDirectory ?? "{{home}}", ["agentRecipe.shellBackend"] = (profile.AgentRecipe?.ShellBackend ?? AgentShellBackend.PowerShell7).ToString(),
        ["agentRecipe.arguments"] = JsonSerializer.Serialize(profile.AgentRecipe?.Arguments ?? [])
    };

    private void LoadSelected(bool animate = false)
    {
        var profile = Catalog?.Profiles.FirstOrDefault(item => item.Id == _selectedId);
        if (profile is null)
        {
            MotionPolicy.Hide(_form, false);
            PreviewProfile = null;
            return;
        }
        _loading = true;
        var values = _host.ProfilesDraft?.EditorValues.GetValueOrDefault(profile.Id) ?? ValuesFrom(profile);
        _text["id"].Text = profile.Id;
        foreach (var (key, input) in _text) if (key != "id") input.Text = values.GetValueOrDefault(key, string.Empty);
        foreach (var (key, input) in _checks) input.IsChecked = bool.TryParse(values.GetValueOrDefault(key), out var value) && value;
        _arguments.Load(ReadArguments(values, "arguments"));
        _agentArguments.Load(ReadArguments(values, "agentRecipe.arguments"));
        _backend.SelectedItem = Enum.TryParse<AgentShellBackend>(values.GetValueOrDefault("agentRecipe.shellBackend"), out var backend) ? backend : AgentShellBackend.PowerShell7;
        _loading = false;
        ValidateAndPreview(profile, values);
        MotionPolicy.Show(_form, animate);
    }

    private static List<string> ReadArguments(Dictionary<string, string> raw, string key)
    {
        try { return JsonSerializer.Deserialize<List<string>>(raw.GetValueOrDefault(key, "[]")) ?? []; }
        catch (JsonException) { return []; }
    }

    private Dictionary<string, string> Capture()
    {
        var raw = _text.Where(pair => pair.Key != "id").ToDictionary(pair => pair.Key, pair => pair.Value.Text);
        foreach (var (key, input) in _checks) raw[key] = (input.IsChecked == true).ToString();
        raw["arguments"] = JsonSerializer.Serialize(_arguments.Values);
        raw["agentRecipe.arguments"] = JsonSerializer.Serialize(_agentArguments.Values);
        raw["agentRecipe.shellBackend"] = (_backend.SelectedItem ?? AgentShellBackend.PowerShell7).ToString()!;
        return raw;
    }

    private bool Build(TerminalProfile source, Dictionary<string, string> raw, out TerminalProfile candidate, out Dictionary<string, string> errors)
    {
        candidate = source.DeepClone();
        errors = [];
        candidate.Name = raw.GetValueOrDefault("name", source.Name);
        candidate.Icon = raw.GetValueOrDefault("icon", source.Icon);
        candidate.ExecutablePath = raw.GetValueOrDefault("executablePath", source.ExecutablePath);
        candidate.Arguments = ReadArguments(raw, "arguments");
        candidate.PostLaunchInput = raw.GetValueOrDefault("postLaunchInput", source.PostLaunchInput);
        candidate.WorkingDirectory = raw.GetValueOrDefault("workingDirectory", source.WorkingDirectory);
        var env = EnvironmentRows.Parse(raw.GetValueOrDefault("environment", string.Empty), out var environment);
        candidate.Environment = environment;
        if (!env.IsValid) errors["environment"] = env.Summary;
        candidate.Terminal.UseGlobalFont = raw.GetValueOrDefault("terminal.useGlobalFont") == "True";
        candidate.Terminal.FontFamily = raw.GetValueOrDefault("terminal.fontFamily", source.Terminal.FontFamily);
        if (double.TryParse(raw.GetValueOrDefault("terminal.fontSize"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) candidate.Terminal.FontSize = size;
        else errors["terminal.fontSize"] = "请输入有效字号。";
        if (int.TryParse(raw.GetValueOrDefault("terminal.scrollbackLines"), out var lines)) candidate.Terminal.ScrollbackLines = lines;
        else errors["terminal.scrollbackLines"] = "请输入有效整数。";
        candidate.RunAsAdministrator = raw.GetValueOrDefault("administrator") == "True";
        candidate.ShellIntegration = raw.GetValueOrDefault("shellIntegration") == "True" ? ShellIntegrationMode.Automatic : ShellIntegrationMode.Disabled;
        candidate.AgentRecipe = raw.GetValueOrDefault("agent") == "True" ? new()
        {
            AgentId = raw.GetValueOrDefault("agentRecipe.agentId", string.Empty), Model = raw.GetValueOrDefault("agentRecipe.model"),
            PermissionMode = raw.GetValueOrDefault("agentRecipe.permissionMode"), WorkingDirectory = raw.GetValueOrDefault("agentRecipe.workingDirectory", "{{home}}"),
            Arguments = ReadArguments(raw, "agentRecipe.arguments"),
            ShellBackend = Enum.TryParse<AgentShellBackend>(raw.GetValueOrDefault("agentRecipe.shellBackend"), out var backend) ? backend : (AgentShellBackend)(-1)
        } : null;
        if (_host.ValidateAgentRecipe(candidate.AgentRecipe) is { } agentError) errors["agentRecipe.agentId"] = agentError;
        var validation = ConfigurationValidator.ValidateProfiles(new() { Profiles = [candidate] });
        foreach (var issue in validation.Issues)
        {
            var key = issue.Path.Replace("profiles[0].", string.Empty, StringComparison.Ordinal);
            if (key == "terminal") key = "terminal.fontSize";
            errors[key] = issue.Message;
        }
        return errors.Count == 0;
    }

    private void ValidateAndPreview(TerminalProfile profile, Dictionary<string, string> raw)
    {
        var valid = Build(profile, raw, out var candidate, out var errors);
        foreach (var (key, block) in _errors) block.Text = errors.GetValueOrDefault(key, string.Empty);
        _summary.Text = string.Join("\n", errors.Where(pair => !_errors.ContainsKey(pair.Key)).Select(pair => pair.Key + ": " + pair.Value));
        var inherit = _checks["terminal.useGlobalFont"].IsChecked == true;
        _text["terminal.fontFamily"].IsEnabled = _text["terminal.fontSize"].IsEnabled = !inherit;
        _checks["administrator"].IsEnabled = ArkTerminal.Terminal.Sessions.Elevation.ElevationSupport.UnavailableReason is null || profile.RunAsAdministrator;
        _checks["administrator"].ToolTip = ArkTerminal.Terminal.Sessions.Elevation.ElevationSupport.UnavailableReason;
        var agent = _checks["agent"].IsChecked == true;
        foreach (var (key, input) in _text) if (key.StartsWith("agentRecipe.", StringComparison.Ordinal)) input.IsEnabled = agent;
        _backend.IsEnabled = _agentArguments.IsEnabled = agent;
        PreviewProfile = valid ? candidate : profile;
        if (valid && _host.ProfilesDraft is not null)
        {
            var index = Catalog.Profiles.FindIndex(item => item.Id == profile.Id);
            Catalog.Profiles[index] = candidate;
        }
        PreviewChanged?.Invoke();
        SchedulePathCheck(valid ? candidate : null);
    }

    private void Changed()
    {
        if (_loading || !_host.CanEdit || _selectedId is null) return;
        _host.EnsureProfilesDraft();
        _host.ProfilesDraft!.SelectedProfileId = _selectedId;
        var raw = Capture();
        if (raw.Where(pair => pair.Key.StartsWith("agentRecipe.", StringComparison.Ordinal)).Any(pair => ArkTerminal.Core.Agents.AgentRecipeRules.ContainsSecret(pair.Value)))
        {
            _debounce?.Cancel();
            _summary.Text = "Agent 字段含凭据，未加入草稿或写盘。请移除后继续。";
            return;
        }
        _host.ProfilesDraft.EditorValues[_selectedId] = raw;
        var profile = Catalog.Profiles.First(item => item.Id == _selectedId);
        ValidateAndPreview(profile, raw);
        _loading = true;
        _list.ItemsSource = Catalog.Profiles;
        _list.SelectedValue = _selectedId;
        _list.Items.Refresh();
        _loading = false;
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        _debounce?.Cancel(); _debounce?.Dispose();
        _debounce = new();
        _ = PersistLaterAsync(_debounce.Token);
    }

    private async Task PersistLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_host.StorageOptions.DraftDebounceMilliseconds, token);
            var result = await _host.PersistProfilesDraftAsync();
            if (!result.Success && !token.IsCancellationRequested) StatusChanged?.Invoke(result.Error ?? "Profile 草稿保存失败。");
        }
        catch (OperationCanceledException) { }
    }

    private void SchedulePathCheck(TerminalProfile? profile)
    {
        _pathCancellation?.Cancel(); _pathCancellation?.Dispose();
        _pathCancellation = new();
        var version = ++_pathVersion;
        if (profile is null) { _path.Text = "请先修正字段错误，再检查路径。"; return; }
        _ = CheckPathAsync(profile.DeepClone(), version, _pathCancellation.Token);
    }

    private async Task CheckPathAsync(TerminalProfile profile, int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            var request = SessionLaunchRequest.FromProfile(profile);
            var result = profile.AgentRecipe is null ? await Task.Run(() => LaunchPathInspector.Inspect(request), token) :
                LaunchPathInspector.Inspect((await _host.Agents.PrepareAsync(SessionLaunchRequest.FromProfile(new TerminalProfile
                {
                    Id = profile.Id, ExecutablePath = profile.ExecutablePath, WorkingDirectory = profile.AgentRecipe.WorkingDirectory,
                    AgentRecipe = profile.AgentRecipe.DeepClone()
                }), token)).Request);
            if (version != _pathVersion || token.IsCancellationRequested) return;
            _path.Text = result.Message + $"\nexecutablePath: {result.ExecutablePath ?? "未解析"}\nworkingDirectory: {result.WorkingDirectory ?? "未解析"}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or InvalidOperationException)
        { if (version == _pathVersion && !token.IsCancellationRequested) _path.Text = "Agent 当前不可启动：" + error.Message; }
    }

    private void Add(bool duplicate)
    {
        if (!_host.CanEdit) return;
        if (duplicate && !Catalog.Profiles.Any(item => item.Id == _selectedId))
        { StatusChanged?.Invoke("请先选择要复制的 Profile。"); return; }
        _host.EnsureProfilesDraft();
        var profile = duplicate ? (Catalog.Profiles.FirstOrDefault(item => item.Id == _selectedId)?.DeepClone() ?? new()) : new TerminalProfile
        { Name = "新 Profile", ExecutablePath = "cmd.exe", Terminal = new() { UseGlobalFont = true } };
        profile.Id = "profile-" + Guid.NewGuid().ToString("N");
        if (duplicate) profile.Name += " 副本";
        Catalog.Profiles.Add(profile);
        _host.ProfilesDraft!.SelectedProfileId = _selectedId = profile.Id;
        Reload(); ScheduleSave();
    }

    private void Delete()
    {
        if (_selectedId is null || !_host.CanEdit) return;
        if (_host.FormalSettings?.DefaultProfileId == _selectedId || _host.Draft?.Settings.DefaultProfileId == _selectedId)
        { StatusChanged?.Invoke("请先更换并保存默认 Profile，再删除该项。"); return; }
        if (MessageBox.Show(Window.GetWindow(this), "从 Profile 目录删除所选项？运行会话保持原样，之后不能用已删除项重启。", "删除 Profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _host.EnsureProfilesDraft();
        Catalog.Profiles.RemoveAll(profile => profile.Id == _selectedId);
        _host.ProfilesDraft!.EditorValues.Remove(_selectedId);
        _host.ProfilesDraft.SelectedProfileId = _selectedId = null;
        Reload(); ScheduleSave(); PreviewChanged?.Invoke();
    }

    public async Task<bool> SaveAsync()
    {
        _debounce?.Cancel();
        if (Capture().Where(pair => pair.Key.StartsWith("agentRecipe.", StringComparison.Ordinal)).Any(pair => ArkTerminal.Core.Agents.AgentRecipeRules.ContainsSecret(pair.Value)))
        { StatusChanged?.Invoke("Agent 字段含凭据，未保存。请先移除。"); return false; }
        if (_host.ProfilesDraft is null) return true;
        foreach (var profile in Catalog.Profiles.ToArray())
        {
            var raw = _host.ProfilesDraft.EditorValues.GetValueOrDefault(profile.Id) ?? ValuesFrom(profile);
            if (!Build(profile, raw, out var candidate, out _))
            {
                _host.ProfilesDraft.SelectedProfileId = _selectedId = profile.Id;
                Reload();
                StatusChanged?.Invoke("Profile 含字段错误，未保存；请修正行内提示。");
                return false;
            }
            Catalog.Profiles[Catalog.Profiles.FindIndex(item => item.Id == profile.Id)] = candidate;
        }
        var result = await _host.SaveProfilesAsync();
        if (!result.Success) _host.Diagnostics.Record(DiagnosticComponent.Profiles, DiagnosticEventKind.WriteFailed);
        StatusChanged?.Invoke(result.Success ? result.Warning ?? "Profile 已保存；新建或重启会话时使用新启动配置。" : result.Error ?? "Profile 保存失败。");
        if (result.Success) { Reload(); CatalogSaved?.Invoke(); }
        return result.Success;
    }

    public async Task<bool> CancelAsync()
    {
        _debounce?.Cancel();
        var result = await _host.CancelProfilesDraftAsync();
        StatusChanged?.Invoke(result.Success ? "Profile 草稿已取消。" : result.Error ?? "取消失败。");
        if (result.Success) { Reload(); PreviewChanged?.Invoke(); }
        return result.Success;
    }

    public void Dispose()
    {
        FontPreview.Detach();
        _debounce?.Cancel(); _debounce?.Dispose();
        _pathCancellation?.Cancel(); _pathCancellation?.Dispose();
    }
}
