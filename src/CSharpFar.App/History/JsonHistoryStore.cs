using System.Text.Json;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.History;

/// <summary>
/// IHistoryStore backed by a JSON file.
/// Loads history at construction; saves after every mutation.
/// All I/O errors are silently swallowed to avoid crashing the app.
/// </summary>
public sealed class JsonHistoryStore : IHistoryStore
{
    private readonly int _maxCommands;
    private readonly int _maxDirectories;
    private readonly int _maxFiles;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly List<CommandHistoryItem>   _commands    = new();
    private readonly List<DirectoryHistoryItem> _directories = new();
    private readonly List<FileHistoryItem>      _files       = new();

    public JsonHistoryStore(
        string? filePath        = null,
        int     maxCommands     = 1000,
        int     maxDirectories  = 500,
        int     maxFiles        = 200)
    {
        _filePath       = filePath ?? DefaultPath();
        _maxCommands    = maxCommands;
        _maxDirectories = maxDirectories;
        _maxFiles       = maxFiles;
        Load();
    }

    // ── IHistoryStore ─────────────────────────────────────────────────────────

    public IReadOnlyList<CommandHistoryItem> GetCommandHistory() => _commands;

    public void AddCommand(CommandHistoryItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Command))
            return;

        _commands.RemoveAll(existing =>
            string.Equals(existing.Command, item.Command, StringComparison.Ordinal));
        _commands.Add(item);
        if (_commands.Count > _maxCommands) _commands.RemoveAt(0);
        Save();
    }

    public IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory() => _directories;

    public void AddDirectory(DirectoryHistoryItem item)
    {
        if (_directories.Count > 0 &&
            string.Equals(_directories[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _directories.Add(item);
        if (_directories.Count > _maxDirectories) _directories.RemoveAt(0);
        Save();
    }

    public IReadOnlyList<FileHistoryItem> GetFileHistory() => _files;

    public void AddFile(FileHistoryItem item)
    {
        if (_files.Count > 0 &&
            string.Equals(_files[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _files.Add(item);
        if (_files.Count > _maxFiles) _files.RemoveAt(0);
        Save();
    }

    // ── persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            string json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<HistoryData>(json, JsonOptions);
            if (data is null) return;
            if (data.Commands    is not null) _commands.AddRange(data.Commands);
            if (data.Directories is not null) _directories.AddRange(data.Directories);
            if (data.Files       is not null) _files.AddRange(data.Files);
            NormalizeCommandHistory();
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void NormalizeCommandHistory()
    {
        if (_commands.Count == 0)
            return;

        var newestFirst = new List<CommandHistoryItem>(_commands.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            var item = _commands[i];
            if (string.IsNullOrWhiteSpace(item.Command))
                continue;

            if (seen.Add(item.Command))
                newestFirst.Add(item);
        }

        _commands.Clear();
        for (int i = newestFirst.Count - 1; i >= 0; i--)
            _commands.Add(newestFirst[i]);

        while (_commands.Count > _maxCommands)
            _commands.RemoveAt(0);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var data = new HistoryData { Commands = _commands, Directories = _directories, Files = _files };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch { /* best effort — never crash the app */ }
    }

    private static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar",
            "history.json");

    // ── DTO ───────────────────────────────────────────────────────────────────

    private sealed class HistoryData
    {
        public List<CommandHistoryItem>?   Commands    { get; set; }
        public List<DirectoryHistoryItem>? Directories { get; set; }
        public List<FileHistoryItem>?      Files       { get; set; }
    }
}
