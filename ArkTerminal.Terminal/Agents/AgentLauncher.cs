using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Sessions;
using ArkTerminal.Terminal.Sessions.Windows;

namespace ArkTerminal.Terminal.Agents;

public sealed record AgentLaunchPlan(SessionLaunchRequest Request, AgentShellChoice Shell, string ExecutablePath, IReadOnlyList<string> Arguments, AgentCapabilities Capabilities)
{
    public string Preview => Shell.Description + "\n" + AgentRecipeRules.Preview(ExecutablePath, Arguments);
}

public sealed class AgentLauncher(AgentAdapterRegistry registry, ISessionManager sessions) : IAgentLauncher
{
    public async Task<AgentLaunchPlan> PrepareAsync(SessionLaunchRequest request, CancellationToken token = default)
    {
        var recipe = request.AgentRecipe ?? throw new ArgumentException("没有 Agent 配方。");
        // Re-read approval hashes at every explicit launch. Cached results never authorize JSON code execution.
        await registry.ReloadAsync(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var entry = registry.Entries.FirstOrDefault(item => item.Definition?.Id == recipe.AgentId && item.Enabled && item.Error is null)
                ?? throw new ArgumentException("Agent 适配器不存在、被禁用或文件已改变；请重新审核启用。");
            var definition = entry.Definition!;
            var path = AgentDetector.FindExecutable(definition, registry.OverridePaths.GetValueOrDefault(definition.Id) ?? definition.OverridePath, token: token).Path
                ?? throw new FileNotFoundException("Agent 未安装或路径已失效；请在 Agent 区修正路径。");
            var directory = ShellLaunchAdapter.ResolveWorkingDirectory(request.WorkingDirectory);
            var arguments = AgentRecipeRules.BuildArguments(definition, recipe, directory);
            var shell = AgentPowerShell.ResolveBackend(recipe.ShellBackend);
            var wrapped = AgentPowerShell.Wrap(request, path, arguments, shell);
            return new AgentLaunchPlan(wrapped, shell, path, arguments, definition.Capabilities);
        }, token).ConfigureAwait(false);
    }

    public async Task<ISession> LaunchAsync(AgentLaunchRecipe recipe, CancellationToken cancellationToken)
    {
        var request = SessionLaunchRequest.FromProfile(new TerminalProfile { Id = "agent-" + recipe.AgentId, ExecutablePath = "pwsh.exe", WorkingDirectory = recipe.WorkingDirectory, AgentRecipe = recipe.DeepClone() });
        var plan = await PrepareAsync(request, cancellationToken).ConfigureAwait(false);
        return await sessions.CreateAsync(plan.Request, cancellationToken).ConfigureAwait(false);
    }
}
