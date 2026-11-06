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
    private readonly Button _enable = new() { Content = Localize.T("Text0252") };
    private readonly Button _disable = new() { Content = Localize.T("Text0253") };
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
        root.Children.Add(new TextBlock { Text = Localize.T("Text0254"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 8) });
        root.Children.Add(new TextBlock { Text = Localize.T("Text0255") + host.Paths.AdaptersDirectory, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        Button(_actions, Localize.T("Text0256"), () => RefreshAsync(false));
        Button(_actions, Localize.T("Text0257"), () => RefreshAsync(true));
        var cancel = new Button { Content = Localize.T("Text0258") }; cancel.Click += (_, _) => _cancellation?.Cancel();
        var toolbar = new WrapPanel(); toolbar.Children.Add(_actions); toolbar.Children.Add(cancel); root.Children.Add(toolbar);
        root.Children.Add(_status); root.Children.Add(_agents); root.Children.Add(_details);
        var jsonActions = new WrapPanel(); jsonActions.Children.Add(_enable); jsonActions.Children.Add(_disable); root.Children.Add(jsonActions);
        _enable.Click += async (_, _) => await RunAsync(() => SetEnabledAsync(true));
        _disable.Click += async (_, _) => await RunAsync(() => SetEnabledAsync(false));
        Label(root, Localize.T("Text0259"), _override);
        var paths = new WrapPanel(); Button(paths, Localize.T("Text0260"), SaveOverrideAsync); root.Children.Add(paths);
        Label(_recipe, Localize.T("Text0261"), _name);
        Label(_recipe, Localize.T("Text0262"), _model);
        Label(_recipe, Localize.T("Text0263"), _permissions);
        Label(_recipe, Localize.T("Text0264"), _backend);
        Label(_recipe, Localize.T("Text0265"), _arguments);
        var recipeActions = new WrapPanel();
        Button(recipeActions, Localize.T("Text0266"), PreviewAsync);
        Button(recipeActions, Localize.T("Text0267"), LaunchAsync);
        Button(recipeActions, Localize.T("Text0268"), CreateProfileAsync);
        _recipe.Children.Add(recipeActions); _recipe.Children.Add(_preview); root.Children.Add(_recipe);
        _recipe.Children.Add(new TextBlock { Text = Localize.T("Text0269"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
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
        catch (OperationCanceledException) { if (!_stopping) _status.Text = Localize.T("Text0270"); }
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

    public void RefreshLocalization()
    {
        ReloadRows();
        Select();
    }

    private async Task RefreshAsync(bool probe)
    {
        _cancellation?.Dispose(); _cancellation = new();
        _actions.IsEnabled = false;
        _agents.IsEnabled = _recipe.IsEnabled = false;
        _status.Text = probe ? Localize.T("Text0271") : Localize.T("Text0272");
        try
        {
            await _host.AgentDetection.DetectAsync(probe, _cancellation.Token);
            if (_stopping) return;
            ReloadRows();
            _status.Text = (probe ? Localize.T("Text0273") : Localize.T("Text0274")) +
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
            var state = entry.Error is not null ? Localize.T("Text0275") : !entry.Enabled ? Localize.T("Text0276") : result is null ? Localize.T("Text0277") : Status(result.Status);
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
            $"{row.Label}\n{row.Entry.Error ?? result?.Reason ?? (row.Entry.Enabled ? Localize.T("Text0843") : Localize.T("Text0844"))}" +
            Localize.F("Text0278", result?.ResolvedPath ?? Localize.T("Text0845"), result?.Source ?? Localize.T("Text0056")) +
            Localize.F("Text0279", result?.Version ?? Localize.T("Text0846"), result?.ProbedAtUtc is { } probedAt ? Localize.ToConfiguredTime(probedAt).ToString("g", Localize.Current.Culture) : Localize.T("Text0847")) +
            Localize.F("Text0280", definition?.Evidence, string.Join(", ", definition?.ExecutableCandidates ?? [])) +
            Localize.F("Text0281", string.Join(", ", definition?.Fields.Arguments.Select(option => option.Flag + (option.TakesValue ? Localize.T("Text0848") : "")) ?? [])) +
            Localize.T("Text0282");
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
        var definition = Selected?.Entry.Definition ?? throw new ArgumentException(Localize.T("Text0283"));
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
            var review = Localize.T("Text0284") +
                AgentRecipeRules.Preview(path ?? string.Join(" / ", definition.ExecutableCandidates), definition.VersionProbe?.Arguments ?? []) +
                Localize.T("Text0285") + JsonSerializer.Serialize(definition, AgentAdapterRegistry.JsonOptions);
            if (!Review(Window.GetWindow(this), Localize.T("Text0286"), review, Localize.T("Text0287"))) return;
        }
        await _host.AgentAdapters.SetEnabledAsync(row.Entry, enabled, CancellationToken.None);
        await RefreshAsync(false);
    }

    private TerminalProfile Profile()
    {
        var definition = Selected?.Entry.Definition ?? throw new ArgumentException(Localize.T("Text0283"));
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
        buttons.Children.Add(yes); buttons.Children.Add(new Button { Content = Localize.T("Text0218"), IsCancel = true });
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 12) });
        dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
    private static void Label(Panel panel, string text, UIElement input)
    { panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 4) }); panel.Children.Add(input); }
    private static string Status(AgentDetectionStatus status) => status switch
    { AgentDetectionStatus.Verified => Localize.T("Text0288"), AgentDetectionStatus.FoundUnverified => Localize.T("Text0289"), AgentDetectionStatus.NotInstalled => Localize.T("Text0290"), AgentDetectionStatus.TimedOut => Localize.T("Text0291"), AgentDetectionStatus.InvalidConfiguration => Localize.T("Text0275"), _ => Localize.T("Text0292") };
}
