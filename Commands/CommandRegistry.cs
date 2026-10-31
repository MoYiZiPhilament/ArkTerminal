using ArkTerminal.Sessions;

namespace ArkTerminal.Commands;

public sealed class CommandContext
{
    public required TabViewModel? CurrentTab { get; init; }
    public required IReadOnlyCollection<TabViewModel> Tabs { get; init; }
    public TabViewModel? GetTab(Guid tabId) => Tabs.FirstOrDefault(tab => tab.TabId == tabId);
}

public sealed class CommandDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Category { get; init; }
    public string Keywords { get; init; } = string.Empty;
    public required Func<CancellationToken, Task> ExecuteAsync { get; init; }
}

public interface ICommandRegistry
{
    IReadOnlyList<CommandDescriptor> Search(string query, int maximumResults = 100);
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<Func<IReadOnlyList<CommandDescriptor>>> _sources = [];
    public void Register(Func<IReadOnlyList<CommandDescriptor>> source) => _sources.Add(source);
    public void Register(CommandDescriptor command) => Register(() => [command]);
    public IReadOnlyList<CommandDescriptor> Search(string query, int maximumResults = 100) => _sources
        .SelectMany(source => source())
        .Where(command => string.IsNullOrWhiteSpace(query) ||
            command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            command.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            command.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        .Take(maximumResults)
        .ToArray();
}
