using System.Text.Json;
using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

/// <summary>
/// Loads and saves the user menu in user-menu.json in the config directory.
/// Creates a default file with sample commands on first run.
/// </summary>
public sealed class UserMenuStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public IReadOnlyList<UserMenuItem> Items { get; private set; }

    public UserMenuStore(string configDirectory)
    {
        _filePath = Path.Combine(configDirectory, "user-menu.json");
        Items = Load();
    }

    /// <summary>Saves a detached snapshot of the supplied menu and updates the runtime menu after a successful write.</summary>
    public void Save(IReadOnlyList<UserMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        UserMenuItem[] snapshot = CloneItems(items);
        WriteFileSafely(snapshot);
        Items = snapshot;
    }

    private IReadOnlyList<UserMenuItem> Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = CreateDefaults();
            TryWriteDefaults(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            List<UserMenuItem> items = JsonSerializer.Deserialize<List<UserMenuItem>>(json, JsonOptions)
                ?? throw new InvalidDataException("User menu file does not contain a JSON array: " + _filePath);
            return CloneItems(items);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("User menu file is invalid: " + _filePath, ex);
        }
    }

    private void TryWriteDefaults(IReadOnlyList<UserMenuItem> items)
    {
        try
        {
            WriteFileSafely(items);
        }
        catch
        {
            // First-run defaults remain available in memory even if the optional initial file cannot be written.
        }
    }

    private void WriteFileSafely(IReadOnlyList<UserMenuItem> items)
    {
        string directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(items, JsonOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Preserve the original save exception; a stale temporary file is preferable to masking it.
        }
    }

    private static UserMenuItem[] CloneItems(IEnumerable<UserMenuItem> items) =>
        items.Select(item => new UserMenuItem
        {
            Title = item.Title,
            Command = item.Command,
        }).ToArray();

    private static List<UserMenuItem> CreateDefaults() =>
    [
        new UserMenuItem { Title = "Open Explorer here",  Command = "explorer \"{panelDir}\"" },
        new UserMenuItem { Title = "List directory",       Command = "dir \"{panelDir}\"" },
        new UserMenuItem { Title = "Copy path to clipboard", Command = "echo {current}| clip" },
    ];
}
