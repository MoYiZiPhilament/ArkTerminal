using ArkTerminal.Core.Profiles;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal.Terminal.Agents;

public interface IAgentLauncher
{
    Task<ISession> LaunchAsync(AgentLaunchRecipe recipe, CancellationToken cancellationToken);
}
