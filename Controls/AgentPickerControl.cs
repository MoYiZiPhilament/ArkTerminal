using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Agents;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Controls;

public sealed class AgentPickerControl : UserControl
{
    private readonly AppHost _host;
    private readonly ComboBox _agents = new() { DisplayMemberPath = "Label", MinWidth = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _override = new();
    private readonly TextBox _name = new();
    private readonly TextBox _model = new();
    private readonly ComboBox _permissions = new();
    private readonly ComboBox _backend = new() { ItemsSource = Enum.GetValues<AgentShellBackend>(), SelectedItem = AgentShellBackend.PowerShell7 };
    private readonly ArgumentListEditor _arguments = new();
    private readonly TextBox _preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxHeight = 220, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _recipe = new();
    private readonly WrapPanel _actions = new();
    private readonly Button _enable = new() { Content = "审核并启用 JSON" };
    private readonly Button _disable = new() { Content = "禁用 JSON" };
    private CancellationTokenSource? _cancellation;
    private Task _work = Task.CompletedTask;
    private bool _stopping;
    public Func<string> WorkingDirectory { get; set; } = () => "{{home}}";
    public Func<TerminalProfile, Task>? LaunchRequested { get; set; }
    public event Action<string>? ProfileCreated;
    private sealed record Row(AgentAdapterEntry Entry, AgentDetectionResult? Result, string Label)
    {
        public override string ToString() => Label;
    }
    private Row? Selected => _agents.SelectedItem as Row;

    public AgentPickerControl(AppHost host)
    {
        _host = host;
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Agent CLI", FontSize = 18, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = "刷新版本会运行 CLI 版本命令。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 8) });
        root.Children.Add(new TextBlock { Text = "自定义适配器目录：" + host.Paths.AdaptersDirectory, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        Button(_actions, "检查路径 / 重载 JSON", () => RefreshAsync(false));
        Button(_actions, "刷新版本", () => RefreshAsync(true));
        var cancel = new Button { Content = "取消检测" }; cancel.Click += (_, _) => _cancellation?.Cancel();
        var toolbar = new WrapPanel(); toolbar.Children.Add(_actions); toolbar.Children.Add(cancel); root.Children.Add(toolbar);
        root.Children.Add(_status); root.Children.Add(_agents); root.Children.Add(_details);
        var jsonActions = new WrapPanel(); jsonActions.Children.Add(_enable); jsonActions.Children.Add(_disable); root.Children.Add(jsonActions);
        _enable.Click += async (_, _) => await RunAsync(() => SetEnabledAsync(true));
        _disable.Click += async (_, _) => await RunAsync(() => SetEnabledAsync(false));
        Label(root, "CLI 完整路径（留空自动检测）", _override);
        var paths = new WrapPanel(); Button(paths, "保存路径覆盖", SaveOverrideAsync); root.Children.Add(paths);
        Label(_recipe, "配方名称", _name);
        Label(_recipe, "模型标识符（留空沿用 CLI）", _model);
        Label(_recipe, "CLI 权限模式（留空沿用 CLI）", _permissions);
        Label(_recipe, "Shell 后端", _backend);
        Label(_recipe, "附加参数（适配器白名单）", _arguments);
        var recipeActions = new WrapPanel();
        Button(recipeActions, "预览实际命令", PreviewAsync);
        Button(recipeActions, "预览并启动", LaunchAsync);
        Button(recipeActions, "创建 Profile 草稿", CreateProfileAsync);
        _recipe.Children.Add(recipeActions); _recipe.Children.Add(_preview); root.Children.Add(_recipe);
        _recipe.Children.Add(new TextBlock { Text = "保存为 Profile 可在下次启动时复用。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Content = root;
        _agents.SelectionChanged += (_, _) => Select();
        void Invalidate() => _preview.Text = string.Empty;
        _name.TextChanged += (_, _) => Invalidate(); _model.TextChanged += (_, _) => Invalidate();
        _arguments.Changed += Invalidate; _permissions.SelectionChanged += (_, _) => Invalidate(); _backend.SelectionChanged += (_, _) => Invalidate();
        ReloadRows();
    }

    private void Button(Panel panel, string content, Func<Task> action)
    {
        var button = new Button { Content = content };
        button.Click += async (_, _) =>
        {
            await RunAsync(action);
        };
        panel.Children.Add(button);
    }

    private Task RunAsync(Func<Task> action)
    {
        if (_stopping || !_work.IsCompleted) return _work;
        return _work = ExecuteAsync(action);
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { if (!_stopping) _status.Text = "检测已取消，探测进程已回收；上次完整结果保留。"; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        { if (!_stopping) _status.Text = error.Message; }
    }

    public Task StartAsync()
    {
        if (_stopping || !_work.IsCompleted) return _work;
        return _work = ExecuteAsync(() => RefreshAsync(false));
    }

    public Task RefreshVersionsAsync()
    {
        if (_stopping || !_work.IsCompleted) return _work;
        return _work = ExecuteAsync(() => RefreshAsync(true));
    }

    private async Task RefreshAsync(bool probe)
    {
        _cancellation?.Dispose(); _cancellation = new();
        _actions.IsEnabled = false;
        _agents.IsEnabled = _recipe.IsEnabled = false;
        _status.Text = probe ? "正在探测版本（最多 2 个并行，每个最多 3 秒 / 64 KiB）…" : "正在轻量检查路径与缓存…";
        try
        {
            await _host.AgentDetection.DetectAsync(probe, _cancellation.Token);
            if (_stopping) return;
            ReloadRows();
            _status.Text = (probe ? "版本刷新完成。" : "路径与缓存检查完成，未运行版本命令。") +
                "\n" + (_host.AgentAdapters.Warning ?? "") + (_host.AgentDetection.CacheWarning ?? "");
        }
        finally { _actions.IsEnabled = _agents.IsEnabled = true; if (!_stopping) Select(); }
    }

    private void ReloadRows()
    {
        var file = Selected?.Entry.FileName; var id = Selected?.Entry.Definition?.Id;
        _agents.ItemsSource = _host.AgentAdapters.Entries.Select((entry, index) =>
        {
            var result = _host.AgentDetection.Results.ElementAtOrDefault(index);
            if (result?.DefinitionId != (entry.Definition?.Id ?? entry.FileName)) result = null;
            var state = entry.Error is not null ? "配置无效" : !entry.Enabled ? "未启用" : result is null ? "尚未检查" : Status(result.Status);
            return new Row(entry, result, (entry.Definition?.DisplayName ?? entry.FileName) + " · " + state);
        }).ToArray();
        _agents.SelectedItem = _agents.Items.OfType<Row>().FirstOrDefault(row => row.Entry.FileName == file && row.Entry.Definition?.Id == id);
        if (_agents.SelectedIndex < 0) _agents.SelectedIndex = 0;
    }

    public void SelectAdapter(string id)
    {
        _agents.SelectedItem = _agents.Items.OfType<Row>().FirstOrDefault(row => row.Entry.Definition?.Id == id);
        BringIntoView(); _agents.Focus();
    }

    private void Select()
    {
        var row = Selected;
        var definition = row?.Entry.Definition;
        var result = row?.Result;
        _details.Text = row is null ? string.Empty :
            $"{row.Label}\n{row.Entry.Error ?? result?.Reason ?? (row.Entry.Enabled ? "尚未检测。" : "JSON 校验成功，等待审核启用。")}" +
            $"\n路径：{result?.ResolvedPath ?? "未发现"} · 来源：{result?.Source ?? "未知"}" +
            $"\n版本：{result?.Version ?? "未验证"} · 探测：{result?.ProbedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未执行"}" +
            $"\n依据：{definition?.Evidence}\n候选：{string.Join("、", definition?.ExecutableCandidates ?? [])}" +
            $"\n支持附加参数：{string.Join("、", definition?.Fields.Arguments.Select(option => option.Flag + (option.TakesValue ? " <值>" : "")) ?? [])}" +
            "\nAgent 执行 / 等待 / 耗时：未知；程序化交接不可用，后续可复制或启动新会话。";
        _enable.IsEnabled = row is { Entry.BuiltIn: false, Entry.Error: null, Entry.Enabled: false } && definition is not null;
        _disable.IsEnabled = row is { Entry.BuiltIn: false, Entry.Enabled: true };
        _override.Text = definition is null ? "" : _host.AgentAdapters.OverridePaths.GetValueOrDefault(definition.Id) ?? definition.OverridePath ?? "";
        _name.Text = definition?.DisplayName ?? ""; _model.Text = ""; _arguments.Load([]);
        _permissions.ItemsSource = new[] { "" }.Concat(definition?.Fields.PermissionModes.Keys ?? Enumerable.Empty<string>()).ToArray();
        _permissions.SelectedIndex = 0;
        _model.IsEnabled = definition?.Fields.Model.Count > 0;
        _recipe.IsEnabled = row?.Entry.Enabled == true && row.Entry.Error is null && definition?.Capabilities.InteractiveInput == true && _host.CanEdit;
        _preview.Text = "";
    }

    private async Task SaveOverrideAsync()
    {
        var definition = Selected?.Entry.Definition ?? throw new ArgumentException("先选择有效适配器。");
        await _host.AgentAdapters.SetOverrideAsync(definition.Id, _override.Text, CancellationToken.None);
        await RefreshAsync(false);
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        if (_stopping || Selected is not { } row) return;
        if (enabled)
        {
            var definition = row.Entry.Definition!;
            var path = await Task.Run(() => AgentDetector.FindExecutable(definition, _host.AgentAdapters.OverridePaths.GetValueOrDefault(definition.Id) ?? definition.OverridePath).Path);
            var review = "启用后，手动刷新可执行版本命令，主动启动可执行配方。请确认来源可信；声明不等于交互能力实测。\n\n版本命令：\n" +
                AgentRecipeRules.Preview(path ?? string.Join(" / ", definition.ExecutableCandidates), definition.VersionProbe?.Arguments ?? []) +
                "\n\n完整声明（启动模板、字段和能力）：\n" + JsonSerializer.Serialize(definition, AgentAdapterRegistry.JsonOptions);
            if (!Review(Window.GetWindow(this), "审核 JSON 适配器", review, "启用此内容")) return;
        }
        await _host.AgentAdapters.SetEnabledAsync(row.Entry, enabled, CancellationToken.None);
        await RefreshAsync(false);
    }

    private TerminalProfile Profile()
    {
        var definition = Selected?.Entry.Definition ?? throw new ArgumentException("先选择有效适配器。");
        var profile = new TerminalProfile
        {
            Id = "agent-" + Guid.NewGuid().ToString("N"), Name = _name.Text, Icon = "✦", ExecutablePath = "pwsh.exe",
            WorkingDirectory = WorkingDirectory(), Terminal = new() { UseGlobalFont = true },
            AgentRecipe = new() { AgentId = definition.Id, Model = _model.Text, PermissionMode = _permissions.SelectedItem as string,
                Arguments = _arguments.Values.ToList(), WorkingDirectory = WorkingDirectory(), ShellBackend = (AgentShellBackend)_backend.SelectedItem }
        };
        if (_host.ValidateAgentRecipe(profile.AgentRecipe) is { } error) throw new ArgumentException(error);
        return profile;
    }

    private async Task PreviewAsync()
    {
        var plan = await _host.Agents.PrepareAsync(SessionLaunchRequest.FromProfile(Profile()));
        _preview.Text = plan.Preview;
    }
    private async Task LaunchAsync()
    {
        if (LaunchRequested is { } launch) await launch(Profile());
    }
    private async Task CreateProfileAsync()
    {
        var profile = Profile();
        var saved = await _host.CreateAgentProfileDraftAsync(profile);
        if (!saved.Success) throw new InvalidOperationException(saved.Error);
        ProfileCreated?.Invoke(profile.Id);
    }

    public async Task StopAsync()
    {
        _stopping = true; _cancellation?.Cancel();
        await _work;
        _cancellation?.Dispose();
    }

    public static bool Review(Window? owner, string title, string text, string accept)
    {
        var dialog = new Window { Owner = owner, Title = title, Width = 720, Height = 570, MinWidth = 440, MinHeight = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        var panel = new DockPanel { Margin = new(18) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var yes = new Button { Content = accept, IsDefault = true }; yes.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(yes); buttons.Children.Add(new Button { Content = "取消", IsCancel = true });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 12) });
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
    private static void Label(Panel panel, string text, UIElement input)
    { panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 4) }); panel.Children.Add(input); }
    private static string Status(AgentDetectionStatus status) => status switch
    { AgentDetectionStatus.Verified => "路径 / 版本已验证", AgentDetectionStatus.FoundUnverified => "已发现未验证", AgentDetectionStatus.NotInstalled => "未安装", AgentDetectionStatus.TimedOut => "探测超时", AgentDetectionStatus.InvalidConfiguration => "配置无效", _ => "不支持 / 未确认" };
}
