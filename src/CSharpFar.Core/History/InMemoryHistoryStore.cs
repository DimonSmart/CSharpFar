using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.History;

public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly int _maxCommands;
    private readonly int _maxDirectories;
    private readonly int _maxFiles;

    private readonly List<CommandHistoryItem>   _commands    = new();
    private readonly List<DirectoryHistoryItem> _directories = new();
    private readonly List<FileHistoryItem>      _files       = new();

    public InMemoryHistoryStore(int maxCommands = 1000, int maxDirectories = 500, int maxFiles = 200)
    {
        _maxCommands    = maxCommands;
        _maxDirectories = maxDirectories;
        _maxFiles       = maxFiles;
    }

    public IReadOnlyList<CommandHistoryItem> GetCommandHistory() => _commands;

    public void AddCommand(CommandHistoryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Command))
            return;

        _commands.RemoveAll(existing =>
            string.Equals(existing.Command, item.Command, StringComparison.Ordinal));
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

    public IReadOnlyList<FileHistoryItem> GetFileHistory() => _files;

    public void AddFile(FileHistoryItem item)
    {
        // Avoid consecutive duplicates
        if (_files.Count > 0 &&
            string.Equals(_files[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _files.Add(item);
        if (_files.Count > _maxFiles)
            _files.RemoveAt(0);
    }
}
