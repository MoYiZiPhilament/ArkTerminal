using System.Windows;
using System.Windows.Controls;
using ArkTerminal.Core.Mcp;
using ArkTerminal.Terminal.Mcp;

namespace ArkTerminal.Controls;

public sealed record McpSessionChoice(Guid Id, string Label)
{
    public override string ToString() => Label;
}

public sealed class McpAccessWindow : Window
{
    private readonly ReadOnlyMcpHost _host;
    private readonly Func<IReadOnlyList<McpSessionChoice>> _sessions;
    private readonly ListBox _connections = new() { MinHeight = 100, MaxHeight = 190, DisplayMemberPath = "Label" };
    private readonly ComboBox _scope = new();
    private readonly Dictionary<McpReadFields, CheckBox> _fields = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBox _configuration = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 100 };
    private int _refreshPending;
    private bool _refreshing;
    private sealed record Row(McpConnectionInfo Info, string Label);

    public McpAccessWindow(Window owner, ReadOnlyMcpHost host, Func<IReadOnlyList<McpSessionChoice>> sessions, Func<Task<string>> configuration)
    {
        Owner = owner; Title = Localize.T("Text0338"); Width = 800; Height = 770; MinWidth = 530; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        _host = host; _sessions = sessions;
        var panel = new DockPanel { Margin = new(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = Localize.T("Text0339"), FontSize = 19, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = Localize.T("Text0340"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        header.Children.Add(_connections);
        header.Children.Add(new TextBlock { Text = Localize.T("Text0341"), Margin = new(0, 8, 0, 4) }); header.Children.Add(_scope);
        foreach (var (field, label) in new[]
        {
            (McpReadFields.Profiles, Localize.T("Text0342")),
            (McpReadFields.Sessions, Localize.T("Text0343")),
            (McpReadFields.Metadata, Localize.T("Text0344")),
            (McpReadFields.WorkingDirectory, Localize.T("Text0345")),
            (McpReadFields.Output, Localize.T("Text0346")),
            (McpReadFields.Selection, Localize.T("Text0347"))
        })
        {
            var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Margin = new(0, 5, 0, 0) };
            _fields.Add(field, box); header.Children.Add(box);
        }
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
        var apply = new Button { Content = Localize.T("Text0348") }; apply.Click += (_, _) => Apply(); actions.Children.Add(apply);
        var revoke = new Button { Content = Localize.T("Text0349") }; revoke.Click += (_, _) => { if (Selected is { } row) _host.SetGrant(row.Info.Id, null); }; actions.Children.Add(revoke);
        var refresh = new Button { Content = Localize.T("Text0350") }; refresh.Click += (_, _) => RefreshScope(); actions.Children.Add(refresh); header.Children.Add(actions);
        header.Children.Add(_status); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new WrapPanel();
        var copy = new Button { Content = Localize.T("Text0351") };
        copy.Click += (_, _) =>
        {
            if (!_configuration.Text.StartsWith('{')) { _status.Text = Localize.T("Text0352"); return; }
            try { Clipboard.SetText(_configuration.Text); _status.Text = Localize.T("Text0353"); }
            catch (System.Runtime.InteropServices.ExternalException) { _status.Text = Localize.T("Text0354"); }
        };
        footer.Children.Add(copy);
        var close = new Button { Content = Localize.T("Text0213"), IsCancel = true }; close.Click += (_, _) => Close(); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(_configuration); Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _connections.SelectionChanged += (_, _) => { if (!_refreshing) LoadGrant(); };
        _host.Changed += Changed;
        Loaded += async (_, _) => { Refresh(); _configuration.Text = await configuration(); };
        Closed += (_, _) => _host.Changed -= Changed;
    }

    private Row? Selected => _connections.SelectedItem as Row;
    private void Changed()
    {
        if (!Dispatcher.HasShutdownStarted && Interlocked.Exchange(ref _refreshPending, 1) == 0)
            Dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref _refreshPending, 0); Refresh(); });
    }

    private void Refresh()
    {
        var id = Selected?.Info.Id;
        var previousGrant = Selected?.Info.Grant;
        _refreshing = true;
        _connections.ItemsSource = _host.Connections.Select(c => new Row(c,
            Localize.F("Text0355", c.ProcessId, c.ClientLabel, Localize.RefreshText(c.Status), c.AllowedReads, c.DeniedReads))).ToArray();
        _connections.SelectedItem = _connections.Items.Cast<Row>().FirstOrDefault(row => row.Info.Id == id) ?? _connections.Items.Cast<Row>().FirstOrDefault();
        _refreshing = false;
        if (Selected?.Info.Id != id || Selected?.Info.Grant != previousGrant) LoadGrant();
        _status.Text = Localize.RefreshText(_host.Status);
    }

    private void RefreshScope()
    {
        var id = (_scope.SelectedItem as McpSessionChoice)?.Id;
        _scope.ItemsSource = _sessions();
        _scope.SelectedItem = _scope.Items.Cast<McpSessionChoice>().FirstOrDefault(s => s.Id == id);
    }

    private void LoadGrant()
    {
        RefreshScope();
        var grant = Selected?.Info.Grant;
        _scope.SelectedItem = _scope.Items.Cast<McpSessionChoice>().FirstOrDefault(s => s.Id == grant?.SessionId);
        foreach (var (field, box) in _fields) box.IsChecked = grant?.Fields.HasFlag(field) == true;
    }

    private void Apply()
    {
        if (Selected is not { } row || _scope.SelectedItem is not McpSessionChoice session) { _status.Text = Localize.T("Text0356"); return; }
        if (!_sessions().Any(s => s.Id == session.Id)) { _status.Text = Localize.T("Text0357"); return; }
        var fields = _fields.Where(p => p.Value.IsChecked == true).Aggregate(McpReadFields.None, (value, p) => value | p.Key);
        if (fields == McpReadFields.None) { _status.Text = Localize.T("Text0358"); return; }
        _host.SetGrant(row.Info.Id, new(session.Id, fields));
    }
}
