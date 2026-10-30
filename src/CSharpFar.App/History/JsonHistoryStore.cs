using System.Text.Json;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.History;

/// <summary>Transactional JSON-backed history shared by local application instances.</summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly int _maxCommands;
    private readonly int _maxDirectories;
    private readonly int _maxFiles;
    private HistoryData _snapshot;

    public JsonHistoryStore(string? filePath = null, int maxCommands = 1000,
        int maxDirectories = 500, int maxFiles = 200)
    {
        _filePath = Path.GetFullPath(filePath ?? DefaultPath());
        _maxCommands = maxCommands;
        _maxDirectories = maxDirectories;
        _maxFiles = maxFiles;
        _snapshot = ReadDocument();
    }

    public IReadOnlyList<CommandHistoryItem> GetCommandHistory()
    {
        lock (_sync)
        {
            Refresh();
            return _snapshot.Commands!.ToArray();
        }
    }

    public IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory()
    {
        lock (_sync)
        {
            Refresh();
            return _snapshot.Directories!.ToArray();
        }
    }

    public IReadOnlyList<FileHistoryItem> GetFileHistory()
    {
        lock (_sync)
        {
            Refresh();
            return _snapshot.Files!.ToArray();
        }
    }

    public void AddCommand(CommandHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Command))
            return;

        Mutate("add-command", data =>
        {
            data.Commands!.RemoveAll(existing =>
                string.Equals(existing.Command, item.Command, StringComparison.Ordinal));
            data.Commands.Add(item);
            Trim(data.Commands, _maxCommands);
            return true;
        });
    }

    public bool RemoveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        return Mutate("remove-command", data =>
            data.Commands!.RemoveAll(existing =>
                string.Equals(existing.Command, command, StringComparison.Ordinal)) > 0);
    }

    public void AddDirectory(DirectoryHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Mutate("add-directory", data =>
        {
            var list = data.Directories!;
            if (list.Count > 0 && string.Equals(list[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
                return false;

            list.Add(item);
            Trim(list, _maxDirectories);
            return true;
        });
    }

    public void AddFile(FileHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Mutate("add-file", data =>
        {
            var list = data.Files!;
            if (list.Count > 0 && string.Equals(list[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
                return false;

            list.Add(item);
            Trim(list, _maxFiles);
            return true;
        });
    }

    private bool Mutate(string operation, Func<HistoryData, bool> change)
    {
        lock (_sync)
        {
            try
            {
                using var fileLock = AtomicHistoryFile.Acquire(_filePath);
                HistoryData current = ReadDocument();
                bool changed = change(current);
                if (changed)
                    AtomicHistoryFile.Write(_filePath, JsonSerializer.Serialize(current, JsonOptions));
                _snapshot = current;
                return changed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or NotSupportedException)
            {
                AtomicHistoryFile.Report("history.json", operation, ex);
                return false;
            }
        }
    }

    private void Refresh()
    {
        try
        {
            _snapshot = ReadDocument();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException or NotSupportedException)
        {
            AtomicHistoryFile.Report("history.json", "read", ex);
        }
    }

    private HistoryData ReadDocument()
    {
        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (FileNotFoundException)
        {
            return new HistoryData { Commands = [], Directories = [], Files = [] };
        }
        catch (DirectoryNotFoundException)
        {
            return new HistoryData { Commands = [], Directories = [], Files = [] };
        }

        try
        {
            var data = JsonSerializer.Deserialize<HistoryData>(json, JsonOptions)
                ?? throw new InvalidDataException("History file does not contain a JSON object: " + _filePath);
            data.Commands ??= [];
            data.Directories ??= [];
            data.Files ??= [];
            NormalizeCommands(data.Commands);
            return data;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("History file is invalid: " + _filePath, ex);
        }
    }

    private void NormalizeCommands(List<CommandHistoryItem> commands)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var newestFirst = new List<CommandHistoryItem>(commands.Count);
        for (int i = commands.Count - 1; i >= 0; i--)
        {
            var item = commands[i];
            if (item is not null && !string.IsNullOrWhiteSpace(item.Command) && seen.Add(item.Command))
                newestFirst.Add(item);
        }

        commands.Clear();
        for (int i = newestFirst.Count - 1; i >= 0; i--)
            commands.Add(newestFirst[i]);
        Trim(commands, _maxCommands);
    }

    private static void Trim<T>(List<T> items, int limit)
    {
        if (items.Count > limit)
            items.RemoveRange(0, items.Count - Math.Max(0, limit));
    }

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar", "history.json");

    private sealed class HistoryData
    {
        public List<CommandHistoryItem>? Commands { get; set; }
        public List<DirectoryHistoryItem>? Directories { get; set; }
        public List<FileHistoryItem>? Files { get; set; }
    }
}
