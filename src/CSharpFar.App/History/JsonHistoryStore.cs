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
    private const int MaxCommands    = 1000;
    private const int MaxDirectories = 500;
    private const int MaxFiles       = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly List<CommandHistoryItem>   _commands    = new();
    private readonly List<DirectoryHistoryItem> _directories = new();
    private readonly List<FileHistoryItem>      _files       = new();

    public JsonHistoryStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath();
        Load();
    }

    // ── IHistoryStore ─────────────────────────────────────────────────────────

    public IReadOnlyList<CommandHistoryItem> GetCommandHistory() => _commands;

    public void AddCommand(CommandHistoryItem item)
    {
        _commands.Add(item);
        if (_commands.Count > MaxCommands) _commands.RemoveAt(0);
        Save();
    }

    public IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory() => _directories;

    public void AddDirectory(DirectoryHistoryItem item)
    {
        if (_directories.Count > 0 &&
            string.Equals(_directories[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _directories.Add(item);
        if (_directories.Count > MaxDirectories) _directories.RemoveAt(0);
        Save();
    }

    public IReadOnlyList<FileHistoryItem> GetFileHistory() => _files;

    public void AddFile(FileHistoryItem item)
    {
        if (_files.Count > 0 &&
            string.Equals(_files[^1].Path, item.Path, StringComparison.OrdinalIgnoreCase))
            return;

        _files.Add(item);
        if (_files.Count > MaxFiles) _files.RemoveAt(0);
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
        }
        catch { /* corrupt file — start fresh */ }
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
