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
            StatusText.Text = Localize.T("Text0029");
        };
        _tabs.PrepareAgentLaunch = PrepareAgentLaunchAsync;
        Loaded += async (_, _) => await _agentPicker.StartAsync();
        _commands.Register(new CommandDescriptor
        {
            Id = "agent.detect", Title = Localize.T("Text0030"), Category = "Agent", Keywords = Localize.T("Text0031"),
            ExecuteAsync = async _ => { ShowAgents(); await _agentPicker.RefreshVersionsAsync(); }
        });
        _commands.Register(() => _host.AgentAdapters.Entries.Where(entry => entry.Enabled && entry.Error is null && entry.Definition is not null).Select(entry => new CommandDescriptor
        {
            Id = "agent.recipe." + entry.Definition!.Id, Title = Localize.T("Text0032") + entry.Definition.DisplayName, Category = "Agent",
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
        var detail = plan.Preview + Localize.T("Text0033") + request.WorkingDirectory +
            Localize.T("Text0034");
        if (!AgentPickerControl.Review(this, Localize.T("Text0035"), detail, Localize.T("Text0036"))) return null;
        tab.AgentBackendDescription = plan.Shell.Description;
        return plan.Request;
    }
}
