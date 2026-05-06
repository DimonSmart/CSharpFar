using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.History;

public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly int _maxCommands;
    private readonly int _maxDirectories;

    private readonly List<CommandHistoryItem>   _commands    = new();
    private readonly List<DirectoryHistoryItem> _directories = new();

    public InMemoryHistoryStore(int maxCommands = 1000, int maxDirectories = 500)
    {
        _maxCommands    = maxCommands;
        _maxDirectories = maxDirectories;
    }

    public IReadOnlyList<CommandHistoryItem> GetCommandHistory() => _commands;

    public void AddCommand(CommandHistoryItem item)
    {
        _commands.Add(item);
        if (_commands.Count > _maxCommands)
            _commands.RemoveAt(0);
    }

    public IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory() => _directories;

    public void AddDirectory(DirectoryHistoryItem item)
    {
        // Avoid consecutive duplicates
        if (_directories.Count > 0 &&
            string.Equals(_directories[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _directories.Add(item);
        if (_directories.Count > _maxDirectories)
            _directories.RemoveAt(0);
    }
}
