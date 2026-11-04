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
        Owner = owner; Title = "只读 MCP · 连接与披露授权"; Width = 800; Height = 770; MinWidth = 530; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        _host = host; _sessions = sessions;
        var panel = new DockPanel { Margin = new(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "每个桥连接单独授权一个会话；授权仅本次连接有效", FontSize = 19, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock { Text = "内核核验的是当前用户的本机桥进程、映像与寿命。客户端自报名称仅供参考，不是可信身份。授权字段可能包含秘密，请按需披露。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        header.Children.Add(_connections);
        header.Children.Add(new TextBlock { Text = "允许的唯一会话（不会自动跟随新标签或重启后的新 Session）", Margin = new(0, 8, 0, 4) }); header.Children.Add(_scope);
        foreach (var (field, label) in new[]
        {
            (McpReadFields.Profiles, "Profile 安全字段：此会话关联 Profile 的 ID、显示名、Shell / Agent 类型"),
            (McpReadFields.Sessions, "会话列表：仅此会话的稳定 ID、生命周期状态、Profile 引用"),
            (McpReadFields.Metadata, "会话元数据：状态、可信启动进程名；Agent 内部任务状态为未知"),
            (McpReadFields.WorkingDirectory, "另行披露 cwd（路径也可能包含敏感信息）"),
            (McpReadFields.Output, "当前输出尾部：最多 200 行 / 64 KiB，尽力脱敏，不读取保存的转录"),
            (McpReadFields.Selection, "明确同意披露该会话的当前主动选区（包括之后新选中的内容），同样有界和尽力脱敏")
        })
        {
            var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, Margin = new(0, 5, 0, 0) };
            _fields.Add(field, box); header.Children.Add(box);
        }
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0) };
        var apply = new Button { Content = "授权所选连接 / 更新范围" }; apply.Click += (_, _) => Apply(); actions.Children.Add(apply);
        var revoke = new Button { Content = "拒绝 / 立即撤销所选连接" }; revoke.Click += (_, _) => { if (Selected is { } row) _host.SetGrant(row.Info.Id, null); }; actions.Children.Add(revoke);
        var refresh = new Button { Content = "刷新会话列表" }; refresh.Click += (_, _) => RefreshScope(); actions.Children.Add(refresh); header.Children.Add(actions);
        header.Children.Add(_status); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new WrapPanel();
        var copy = new Button { Content = "复制本实例 stdio 配置" };
        copy.Click += (_, _) =>
        {
            if (!_configuration.Text.StartsWith('{')) { _status.Text = "当前入口尚不可用，未复制配置。"; return; }
            try { Clipboard.SetText(_configuration.Text); _status.Text = "配置已复制；请手动加入所选客户端。本实例退出后需重新复制配置。"; }
            catch (System.Runtime.InteropServices.ExternalException) { _status.Text = "剪贴板当前不可用，未复制。"; }
        };
        footer.Children.Add(copy);
        var close = new Button { Content = "关闭", IsCancel = true }; close.Click += (_, _) => Close(); footer.Children.Add(close);
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
            $"桥 PID {c.ProcessId} · 自报：{c.ClientLabel} · {c.Status} · 读 {c.AllowedReads} / 拒绝 {c.DeniedReads}")).ToArray();
        _connections.SelectedItem = _connections.Items.Cast<Row>().FirstOrDefault(row => row.Info.Id == id) ?? _connections.Items.Cast<Row>().FirstOrDefault();
        _refreshing = false;
        if (Selected?.Info.Id != id || Selected?.Info.Grant != previousGrant) LoadGrant();
        _status.Text = _host.Status;
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
        if (Selected is not { } row || _scope.SelectedItem is not McpSessionChoice session) { _status.Text = "先选择连接和一个会话。"; return; }
        if (!_sessions().Any(s => s.Id == session.Id)) { _status.Text = "会话已关闭，请刷新范围。"; return; }
        var fields = _fields.Where(p => p.Value.IsChecked == true).Aggregate(McpReadFields.None, (value, p) => value | p.Key);
        if (fields == McpReadFields.None) { _status.Text = "请选择允许披露的字段，或使用拒绝 / 撤销。"; return; }
        _host.SetGrant(row.Info.Id, new(session.Id, fields));
    }
}
