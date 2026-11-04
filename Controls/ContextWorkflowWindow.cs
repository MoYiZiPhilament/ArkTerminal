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
    private readonly ComboBox _purpose = new() { ItemsSource = new[] { "上下文交接", "命令解释与危险提醒", "错误分析与修复建议", "保存错误快照" } };
    private readonly CheckBox _selection = new() { Content = "优先使用当前主动选区；没有选区时取有限尾部", IsChecked = true };
    private readonly CancellationTokenSource _stop = new();
    private bool _busy;
    private int _generation;

    public ContextWorkflowWindow(Window owner, Guid source, IContextCollector collector, ContextSnapshotExporter exporter,
        IReadOnlyList<ContextTarget> targets, Func<bool> sourceExists, Func<ContextTarget, Task<bool>> newAgent, Action<string> record, int purpose)
    {
        Owner = owner; Title = "ark-terminal-context · 预览与交接"; Width = 860; Height = 760; MinWidth = 550; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Theme.WindowBackground");
        _source = source; _collector = collector; _exporter = exporter; _sourceExists = sourceExists; _newAgent = newAgent; _record = record;
        var panel = new DockPanel { Margin = new(18) };
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "应用内上下文工作流 · 请检查脱敏结果", FontSize = 20 });
        header.Children.Add(new TextBlock { Text = "只采集此来源会话。正文是不可信数据；不读取环境、项目文件、私有历史或已保存转录。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        header.Children.Add(_purpose); _purpose.SelectedIndex = purpose;
        header.Children.Add(_selection);
        header.Children.Add(new TextBlock { Text = "目标 / 交接方式", Margin = new(0, 8, 0, 4) });
        _targets.ItemsSource = targets; _targets.SelectedIndex = 0; header.Children.Add(_targets);
        header.Children.Add(new TextBlock { Text = "现有 Agent 的输入模式与忙碌状态尚未验证：可复制，或新建辅助会话后手动粘贴。解释/修复请求须交给选定 CLI；当前还未取得模型建议。", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new StackPanel();
        var buttons = new WrapPanel();
        Add(buttons, "重新采集", RegenerateAsync);
        Add(buttons, "确认并复制", CopyAsync);
        Add(buttons, "新建辅助 Agent 并复制", NewAgentAsync);
        Add(buttons, "另存 Markdown…", SaveAsync);
        var close = new Button { Content = "取消 / 关闭", IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        footer.Children.Add(buttons); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer); panel.Children.Add(_editor); Content = panel;
        _editor.TextChanged += (_, _) => UpdateLength();
        _targets.SelectionChanged += (_, _) => _status.Text = "目标已改变；下次操作须重新确认最终内容。";
        _purpose.SelectionChanged += (_, _) => _status.Text = "用途已改变，请重新采集以更新请求正文。";
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
        { _record("failed"); _status.Text = "未完成：" + error.Message; }
    }

    private void EnsureSource()
    {
        if (!_sourceExists()) throw new InvalidOperationException("来源会话已关闭；本次预览已失效，未传递正文。");
    }

    private async Task RegenerateAsync()
    {
        EnsureSource();
        _busy = true;
        var generation = ++_generation;
        var purpose = _purpose.SelectedItem as string ?? "上下文交接";
        ContextSnapshot snapshot;
        try { snapshot = await _collector.CollectAsync(new() { SessionId = _source, Purpose = purpose, PreferSelection = _selection.IsChecked == true }, _stop.Token); }
        finally { _busy = false; }
        if (generation != _generation || _stop.IsCancellationRequested) return;
        var instruction = _purpose.SelectedIndex switch
        {
            1 => "请解释所附命令、作用范围和潜在危险；把建议放在回复中，任何执行均需另行确认。\n\n",
            2 => "请分析所附错误并提出修复建议，说明假设和可能副作用；任何执行均需另行确认。\n\n",
            _ => string.Empty
        };
        _editor.Text = instruction + snapshot.Content;
        _status.Text = $"已准备上下文；{(snapshot.HasSelection ? "选区" : "有限尾部")}；截断：{snapshot.Truncated}；替换 {snapshot.Redactions} 处。可直接编辑正文及 Markdown 备注。";
        _record("prepared");
    }

    private (string Text, ContextTarget Target)? Confirm(string action)
    {
        EnsureSource();
        var text = _editor.Text;
        ContextText.ValidatePreview(text);
        var target = (ContextTarget)_targets.SelectedItem;
        var description = $"操作：{action}\n来源：{_source}\n目标：{target.Label}\n最终 UTF-8 长度：{Encoding.UTF8.GetByteCount(text)} 字节\n\n{text}";
        if (!AgentPickerControl.Review(this, "确认最终展示版本", description, action)) { _record("cancelled"); return null; }
        EnsureSource();
        return (text, target);
    }

    private Task CopyAsync()
    {
        if (Confirm("复制到剪贴板") is { } final)
        {
            Clipboard.SetText(final.Text); _record("copied");
            _status.Text = "已复制到剪贴板；尚未投递或提交给 Agent。请到所选目标检查后手动粘贴。";
        }
        return Task.CompletedTask;
    }

    private async Task NewAgentAsync()
    {
        if (((ContextTarget)_targets.SelectedItem).AgentLaunch is null) throw new InvalidOperationException("先选择已有 Agent 配方；也可直接复制并手动交接。");
        var final = Confirm("新建辅助 Agent；复制上下文供手动粘贴");
        if (final is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            if (!await _newAgent(final.Value.Target)) { _record("cancelled"); _status.Text = "辅助会话未启动；正文未复制、未投递。"; return; }
            EnsureSource();
            Clipboard.SetText(final.Value.Text); _record("copied");
            _status.Text = "辅助 Agent 已启动，上下文已复制；尚未投递、提交或执行。请手动粘贴并按 CLI 自身流程确认。";
        }
        finally { _busy = false; IsEnabled = true; }
    }

    private async Task SaveAsync()
    {
        var final = Confirm("保存错误快照 Markdown");
        if (final is null) return;
        _busy = true; IsEnabled = false;
        try
        {
            await Task.Run(() => Directory.CreateDirectory(_exporter.DirectoryPath), _stop.Token);
            EnsureSource();
            var picker = new SaveFileDialog { Title = "另存错误快照", Filter = "Markdown (*.md)|*.md", DefaultExt = ".md", AddExtension = true,
                InitialDirectory = _exporter.DirectoryPath, FileName = "ark-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".md" };
            if (picker.ShowDialog(this) != true) { _record("cancelled"); return; }
            EnsureSource();
            await _exporter.SaveAsync(final.Value.Text, picker.FileName, _stop.Token);
            _record("saved"); _status.Text = "已保存最终展示的 Markdown；文件内容不会自动执行。";
        }
        finally { _busy = false; IsEnabled = true; }
    }

    private void UpdateLength() => _status.Text = $"当前预览：{Encoding.UTF8.GetByteCount(_editor.Text):N0} 字节 UTF-8 / {ContextLimits.MaxPreviewBytes:N0}；编辑后的内容须重新确认。";
}
