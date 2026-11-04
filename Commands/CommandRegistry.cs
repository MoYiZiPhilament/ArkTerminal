using ArkTerminal.Sessions;
using System.Windows.Input;

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
    public string Shortcut { get; init; } = "未绑定";
    public CommandScope Scope { get; init; }
    public IReadOnlyList<ShortcutGesture> Gestures { get; init; } = [];
    public Func<bool> CanExecute { get; init; } = () => true;
    public required Func<CancellationToken, Task> ExecuteAsync { get; init; }
}

public enum CommandScope { Workbench, Terminal }
public readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers);

public interface ICommandRegistry
{
    IReadOnlyList<CommandDescriptor> Search(string query, int maximumResults = 100);
}

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<Func<IReadOnlyList<CommandDescriptor>>> _sources = [];
    public void Register(Func<IReadOnlyList<CommandDescriptor>> source) => _sources.Add(source);
    public void Register(CommandDescriptor command) => Register(() => [command]);
    public CommandDescriptor? Find(string id) => _sources.SelectMany(source => source()).FirstOrDefault(command => command.Id == id);
    public CommandDescriptor? Match(Key key, ModifierKeys modifiers, bool terminalFocused) => _sources.SelectMany(source => source())
        .FirstOrDefault(command => (command.Scope != CommandScope.Terminal || terminalFocused) && command.CanExecute() && command.Gestures.Contains(new(key, modifiers)));
    public IReadOnlyList<CommandDescriptor> Search(string query, int maximumResults = 100) => _sources
        .SelectMany(source => source())
        .Where(command => string.IsNullOrWhiteSpace(query) ||
            command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            command.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            command.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
        .Take(maximumResults)
        .ToArray();
}
