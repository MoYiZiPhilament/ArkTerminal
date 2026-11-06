using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Context;
using ArkTerminal.Sessions;
using Microsoft.Win32;

namespace ArkTerminal.Controls;

public sealed record ContextTarget(string Label, TabLaunchConfiguration? AgentLaunch = null, Guid? ExistingSessionId = null)
{
    public override string ToString() => Label;
}

public sealed class ContextWorkflowWindow : Window
{
    private readonly Guid _source;
    private readonly IContextCollector _collector;
    private readonly ContextSnapshotExporter _exporter;
    private readonly Func<bool> _sourceExists;
    private readonly Func<ContextTarget, Task<bool>> _newAgent;
    private readonly Action<string> _record;
    private readonly TextBox _editor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = ContextLimits.MaxPreviewBytes };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly ComboBox _targets = new();
    private readonly ComboBox _purpose = new() { ItemsSource = new[] { Localize.T("Text0305"), Localize.T("Text0306"), Localize.T("Text0307"), Localize.T("Text0308") } };
    private readonly CheckBox _selection = new() { Content = Localize.T("Text0309"), IsChecked = true };
    private readonly CancellationTokenSource _stop = new();
    private bool _busy;
    private int _generation;

    public ContextWorkflowWindow(Window owner, Guid source, IContextCollector collector, ContextSnapshotExporter exporter,
        IReadOnlyList<ContextTarget> targets, Func<bool> sourceExists, Func<ContextTarget, Task<bool>> newAgent, Action<string> record, int purpose)
    {
        Owner = owner; Title = Localize.T("Text0310"); Width = 860; Height = 760; MinWidth = 550; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        _source = source; _collector = collector; _exporter = exporter; _sourceExists = sourceExists; _newAgent = newAgent; _record = record;
        var panel = new DockPanel { Margin = new(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Localize.T("Text0311"), FontSize = 20 });
        header.Children.Add(new TextBlock { Text = Localize.T("Text0312"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        header.Children.Add(_purpose); _purpose.SelectedIndex = purpose;
        header.Children.Add(_selection);
        header.Children.Add(new TextBlock { Text = Localize.T("Text0313"), Margin = new(0, 8, 0, 4) });
        _targets.ItemsSource = targets; _targets.SelectedIndex = 0; header.Children.Add(_targets);
        header.Children.Add(new TextBlock { Text = Localize.T("Text0314"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel();
        var buttons = new WrapPanel();
        Add(buttons, Localize.T("Text0315"), RegenerateAsync);
        Add(buttons, Localize.T("Text0316"), CopyAsync);
        Add(buttons, Localize.T("Text0317"), NewAgentAsync);
        Add(buttons, Localize.T("Text0318"), SaveAsync);
        var close = new Button { Content = Localize.T("Text0301"), IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        footer.Children.Add(buttons); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(_editor); Content = panel;
        _editor.TextChanged += (_, _) => UpdateLength();
        _targets.SelectionChanged += (_, _) => _status.Text = Localize.T("Text0319");
        _purpose.SelectionChanged += (_, _) => _status.Text = Localize.T("Text0320");
        Loaded += async (_, _) => await RunAsync(RegenerateAsync);
        Closed += (_, _) => { _generation++; _stop.Cancel(); _editor.Clear(); _stop.Dispose(); };
    }

    private void Add(Panel panel, string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) =>
        {
            await RunAsync(action);
        };
        panel.Children.Add(button);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { _record("failed"); _status.Text = Localize.T("Text0321") + error.Message; }
    }

    private void EnsureSource()
    {
        if (!_sourceExists()) throw new InvalidOperationException(Localize.T("Text0322"));
    }

    private async Task RegenerateAsync()
    {
        EnsureSource();
        _busy = true;
        var generation = ++_generation;
        var purpose = _purpose.SelectedItem as string ?? Localize.T("Text0305");
        ContextSnapshot snapshot;
        try { snapshot = await _collector.CollectAsync(new() { SessionId = _source, Purpose = purpose, PreferSelection = _selection.IsChecked == true }, _stop.Token); }
        finally { _busy = false; }
        if (generation != _generation || _stop.IsCancellationRequested) return;
        var instruction = _purpose.SelectedIndex switch
        {
            1 => Localize.T("Text0323"),
            2 => Localize.T("Text0324"),
            _ => string.Empty
        };
        _editor.Text = instruction + snapshot.Content;
        _status.Text = Localize.F("Text0325", (snapshot.HasSelection ? Localize.T("Text0841") : Localize.T("Text0842")), snapshot.Truncated, snapshot.Redactions);
        _record("prepared");
    }

    private (string Text, ContextTarget Target)? Confirm(string action)
    {
        EnsureSource();
        var text = _editor.Text;
        ContextText.ValidatePreview(text);
        var target = (ContextTarget)_targets.SelectedItem;
        var description = Localize.F("Text0326", action, _source, target.Label, Encoding.UTF8.GetByteCount(text), text);
        if (!AgentPickerControl.Review(this, Localize.T("Text0327"), description, action)) { _record("cancelled"); return null; }
        EnsureSource();
        return (text, target);
    }

    private Task CopyAsync()
    {
        if (Confirm(Localize.T("Text0328")) is { } final)
        {
            Clipboard.SetText(final.Text); _record("copied");
            _status.Text = Localize.T("Text0329");
        }
        return Task.CompletedTask;
    }

    private async Task NewAgentAsync()
    {
        if (((ContextTarget)_targets.SelectedItem).AgentLaunch is null) throw new InvalidOperationException(Localize.T("Text0330"));
        var final = Confirm(Localize.T("Text0331"));
        if (final is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            if (!await _newAgent(final.Value.Target)) { _record("cancelled"); _status.Text = Localize.T("Text0332"); return; }
            EnsureSource();
            Clipboard.SetText(final.Value.Text); _record("copied");
            _status.Text = Localize.T("Text0333");
        }
        finally { _busy = false; IsEnabled = true; }
    }

    private async Task SaveAsync()
    {
        var final = Confirm(Localize.T("Text0334"));
        if (final is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            await Task.Run(() => Directory.CreateDirectory(_exporter.DirectoryPath), _stop.Token);
            EnsureSource();
            var picker = new SaveFileDialog { Title = Localize.T("Text0335"), Filter = "Markdown (*.md)|*.md", DefaultExt = ".md", AddExtension = true,
                InitialDirectory = _exporter.DirectoryPath, FileName = "ark-error-" + Localize.ToConfiguredTime(DateTimeOffset.UtcNow).ToString("yyyyMMdd-HHmmss", Localize.Current.Culture) + ".md" };
            if (picker.ShowDialog(this) != true) { _record("cancelled"); return; }
            EnsureSource();
            await _exporter.SaveAsync(final.Value.Text, picker.FileName, _stop.Token);
            _record("saved"); _status.Text = Localize.T("Text0336");
        }
        finally { _busy = false; IsEnabled = true; }
    }

    private void UpdateLength() => _status.Text = Localize.F("Text0337", Encoding.UTF8.GetByteCount(_editor.Text), ContextLimits.MaxPreviewBytes);
}
