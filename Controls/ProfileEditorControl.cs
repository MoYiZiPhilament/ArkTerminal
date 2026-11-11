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
    public Rendering.DirectWriteTerminalControl FontPreview { get; } = new() { Height = 110, IsHitTestVisible = false, IsInputEnabled = false };
    public string? SelectedProfileId => _selectedId;
    private ProfileCatalog Catalog => _host.ProfilesDraft?.Catalog ?? _host.Catalog!;

    public ProfileEditorControl(AppHost host)
    {
        _host = host;
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Profile", FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock { Text = Localize.T("Text0359"), Margin = new(0, 6, 0, 8) });
        root.Children.Add(_list);
        var actions = new WrapPanel { Margin = new(0, 8, 0, 12) };
        AddButton(actions, Localize.T("Text0360"), () => Add(false));
        AddButton(actions, Localize.T("Text0361"), () => Add(true));
        AddButton(actions, Localize.T("Text0294"), Delete);
        root.Children.Add(actions);
        AddText("id", Localize.T("Text0362")).IsReadOnly = true;
        AddText("name", Localize.T("Text0363"));
        AddText("icon", Localize.T("Text0364"));
        AddText("executablePath", Localize.T("Text0365"));
        AddLabel(Localize.T("Text0366"), _arguments);
        AddText("postLaunchInput", Localize.T("Text0367"), true);
        AddText("workingDirectory", Localize.T("Text0368"));
        AddText("environment", Localize.T("Text0369"), true);
        _form.Children.Add(_path);
        AddCheck("terminal.useGlobalFont", Localize.T("Text0370"));
        AddText("terminal.fontFamily", Localize.T("Text0371"));
        AddText("terminal.fontSize", Localize.T("Text0372"));
        _form.Children.Add(new RoundedBorder { CornerRadius = new(10), Child = FontPreview });
        AddText("terminal.scrollbackLines", Localize.T("Text0373"));
        AddCheck("administrator", Localize.T("Text0374"));
        AddCheck("shellIntegration", Localize.T("Text0375"));
        AddCheck("agent", Localize.T("Text0376"));
        AddText("agentRecipe.agentId", Localize.T("Text0377"));
        AddText("agentRecipe.model", Localize.T("Text0378"));
        AddText("agentRecipe.permissionMode", Localize.T("Text0379"));
        AddText("agentRecipe.workingDirectory", Localize.T("Text0380"));
        AddLabel(Localize.T("Text0381"), _backend);
        AddLabel(Localize.T("Text0382"), _agentArguments);
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
        else errors["terminal.fontSize"] = Localize.T("Text0383");
        if (int.TryParse(raw.GetValueOrDefault("terminal.scrollbackLines"), out var lines)) candidate.Terminal.ScrollbackLines = lines;
        else errors["terminal.scrollbackLines"] = Localize.T("Text0384");
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
            _summary.Text = Localize.T("Text0385");
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
            if (!result.Success && !token.IsCancellationRequested) StatusChanged?.Invoke(result.Error ?? Localize.T("Text0386"));
        }
        catch (OperationCanceledException) { }
    }

    private void SchedulePathCheck(TerminalProfile? profile)
    {
        _pathCancellation?.Cancel(); _pathCancellation?.Dispose();
        _pathCancellation = new();
        var version = ++_pathVersion;
        if (profile is null) { _path.Text = Localize.T("Text0387"); return; }
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
            _path.Text = result.Message + $"\nexecutablePath: {result.ExecutablePath ?? Localize.T("Text0840")}\nworkingDirectory: {result.WorkingDirectory ?? Localize.T("Text0840")}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or InvalidOperationException)
        { if (version == _pathVersion && !token.IsCancellationRequested) _path.Text = Localize.T("Text0388") + error.Message; }
    }

    private void Add(bool duplicate)
    {
        if (!_host.CanEdit) return;
        if (duplicate && !Catalog.Profiles.Any(item => item.Id == _selectedId))
        { StatusChanged?.Invoke(Localize.T("Text0389")); return; }
        _host.EnsureProfilesDraft();
        var profile = duplicate ? (Catalog.Profiles.FirstOrDefault(item => item.Id == _selectedId)?.DeepClone() ?? new()) : new TerminalProfile
        { Name = Localize.T("Text0390"), ExecutablePath = "cmd.exe", Terminal = new() { UseGlobalFont = true } };
        profile.Id = "profile-" + Guid.NewGuid().ToString("N");
        if (duplicate) profile.Name += Localize.T("Text0391");
        Catalog.Profiles.Add(profile);
        _host.ProfilesDraft!.SelectedProfileId = _selectedId = profile.Id;
        Reload(); ScheduleSave();
    }

    private void Delete()
    {
        if (_selectedId is null || !_host.CanEdit) return;
        if (_host.FormalSettings?.DefaultProfileId == _selectedId || _host.Draft?.Settings.DefaultProfileId == _selectedId)
        { StatusChanged?.Invoke(Localize.T("Text0392")); return; }
        if (MessageBox.Show(Window.GetWindow(this), Localize.T("Text0393"), Localize.T("Text0394"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        { StatusChanged?.Invoke(Localize.T("Text0395")); return false; }
        if (_host.ProfilesDraft is null) return true;
        foreach (var profile in Catalog.Profiles.ToArray())
        {
            var raw = _host.ProfilesDraft.EditorValues.GetValueOrDefault(profile.Id) ?? ValuesFrom(profile);
            if (!Build(profile, raw, out var candidate, out _))
            {
                _host.ProfilesDraft.SelectedProfileId = _selectedId = profile.Id;
                Reload();
                StatusChanged?.Invoke(Localize.T("Text0396"));
                return false;
            }
            Catalog.Profiles[Catalog.Profiles.FindIndex(item => item.Id == profile.Id)] = candidate;
        }
        var result = await _host.SaveProfilesAsync();
        if (!result.Success) _host.Diagnostics.Record(DiagnosticComponent.Profiles, DiagnosticEventKind.WriteFailed);
        StatusChanged?.Invoke(result.Success ? result.Warning ?? Localize.T("Text0397") : result.Error ?? Localize.T("Text0398"));
        if (result.Success) { Reload(); CatalogSaved?.Invoke(); }
        return result.Success;
    }

    public async Task<bool> CancelAsync()
    {
        _debounce?.Cancel();
        var result = await _host.CancelProfilesDraftAsync();
        StatusChanged?.Invoke(result.Success ? Localize.T("Text0399") : result.Error ?? Localize.T("Text0400"));
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
