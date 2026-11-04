using System.Windows;
using ArkTerminal.Commands;
using ArkTerminal.Controls;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal;

public partial class MainWindow
{
    private AgentPickerControl _agentPicker = null!;

    private void InitializeStageEight()
    {
        _agentPicker = new(_host) { WorkingDirectory = () => DirectoryInput.Text };
        AgentPickerHost.Content = _agentPicker;
        _agentPicker.LaunchRequested = profile => LaunchProfileAsync(profile, DirectoryInput.Text);
        _agentPicker.ProfileCreated += id =>
        {
            _profileEditor.SelectProfile(id);
            ShowSettings();
            SettingsCategories.SelectedIndex = 2;
            Dispatcher.BeginInvoke(() => _profileEditor.BringIntoView());
            StatusText.Text = "Agent 配方已加入独立 Profile 草稿；检查后点击保存 Profile。";
        };
        _tabs.PrepareAgentLaunch = PrepareAgentLaunchAsync;
        Loaded += async (_, _) => await _agentPicker.StartAsync();
        _commands.Register(new CommandDescriptor
        {
            Id = "agent.detect", Title = "刷新 Agent CLI 版本", Category = "Agent", Keywords = "检测 codex claude kimi opencode agy codebuddy dsh",
            ExecuteAsync = async _ => { ShowAgents(); await _agentPicker.RefreshVersionsAsync(); }
        });
        _commands.Register(() => _host.AgentAdapters.Entries.Where(entry => entry.Enabled && entry.Error is null && entry.Definition is not null).Select(entry => new CommandDescriptor
        {
            Id = "agent.recipe." + entry.Definition!.Id, Title = "编辑 / 启动 Agent：" + entry.Definition.DisplayName, Category = "Agent",
            ExecuteAsync = _ => { ShowAgents(); _agentPicker.SelectAdapter(entry.Definition.Id); return Task.CompletedTask; }
        }).ToArray());
    }

    private void ShowAgents()
    {
        _settingsVisible = false;
        _tabs.ShowNewPage();
        ShowActiveWorkbench();
        _agentPicker.BringIntoView();
    }

    private async Task<SessionLaunchRequest?> PrepareAgentLaunchAsync(Sessions.TabViewModel tab, SessionLaunchRequest request)
    {
        var plan = await _host.Agents.PrepareAsync(request);
        if (_closing || _allowClose) return null;
        var detail = plan.Preview + "\n\n目录：" + request.WorkingDirectory +
            "\n环境继承当前进程，值不显示或写入检测日志。关闭标签结束本会话的进程树。\nAgent 的任务状态与耗时在没有可靠信号时显示未知。";
        if (!AgentPickerControl.Review(this, "确认 Agent 启动", detail, "启动")) return null;
        tab.AgentBackendDescription = plan.Shell.Description;
        return plan.Request;
    }
}
