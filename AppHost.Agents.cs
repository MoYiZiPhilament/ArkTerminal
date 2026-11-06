using ArkTerminal.Core.Agents;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Storage;
using ArkTerminal.Terminal.Agents;

namespace ArkTerminal;

public sealed partial class AppHost
{
    public AgentAdapterRegistry AgentAdapters { get; private set; } = null!;
    public AgentDetector AgentDetection { get; private set; } = null!;
    public AgentLauncher Agents { get; private set; } = null!;

    private async Task InitializeAgentsAsync()
    {
        AgentAdapters = new(Paths);
        await AgentAdapters.ReloadAsync(_lifetime.Token);
        AgentDetection = new(Paths, AgentAdapters);
        Agents = new(AgentAdapters, _sessionManager);
    }

    public string? ValidateAgentRecipe(AgentLaunchRecipe? recipe)
    {
        if (recipe is null) return null;
        try
        {
            var definition = AgentAdapters.Entries.FirstOrDefault(entry => entry.Enabled && entry.Error is null && entry.Definition?.Id == recipe.AgentId)?.Definition
                ?? throw new ArgumentException(Localize.T("Text0004"));
            AgentRecipeRules.BuildArguments(definition, recipe, recipe.WorkingDirectory);
            return null;
        }
        catch (ArgumentException error) { return error.Message; }
    }

    private string? ValidateAgentProfiles()
    {
        if (ProfilesDraft is null) return null;
        foreach (var profile in ProfilesDraft.Catalog.Profiles)
        {
            var formal = Catalog?.Profiles.FirstOrDefault(item => item.Id == profile.Id);
            // Preserve untouched schema-1 recipes from Stage 6; launch still validates every field.
            if (ValidateAgentRecipe(profile.AgentRecipe) is { } error && !SameDocument(profile.AgentRecipe, formal?.AgentRecipe)) return error;
            if (ProfilesDraft.EditorValues.TryGetValue(profile.Id, out var values) &&
                values.Where(pair => pair.Key.StartsWith("agentRecipe.", StringComparison.Ordinal)).Any(pair => AgentRecipeRules.ContainsSecret(pair.Value)))
                return Localize.T("Text0005");
        }
        return null;
    }

    public async Task<DocumentSaveResult> CreateAgentProfileDraftAsync(TerminalProfile profile)
    {
        if (ValidateAgentRecipe(profile.AgentRecipe) is { } error) return Failure(error);
        EnsureProfilesDraft();
        if (ProfilesDraft!.Catalog.Profiles.Count >= 100) return Failure(Localize.T("Text0006"));
        ProfilesDraft.Catalog.Profiles.Add(profile.DeepClone());
        ProfilesDraft.SelectedProfileId = profile.Id;
        return await PersistProfilesDraftAsync();
    }
}
