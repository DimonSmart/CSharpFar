using System.Text.Json;
using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

/// <summary>
/// Loads the user menu from user-menu.json in the config directory.
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

    public IReadOnlyList<UserMenuItem> Items { get; }

    public UserMenuStore(string configDirectory)
    {
        _filePath = Path.Combine(configDirectory, "user-menu.json");
        Items = Load();
    }

    // ── private ───────────────────────────────────────────────────────────────

    private IReadOnlyList<UserMenuItem> Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = CreateDefaults();
            WriteFile(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<UserMenuItem>>(json, JsonOptions)
                ?? throw new InvalidDataException("User menu file does not contain a JSON array: " + _filePath);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("User menu file is invalid: " + _filePath, ex);
        }
    }

    private void WriteFile(IReadOnlyList<UserMenuItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch { /* best effort */ }
    }

    private static List<UserMenuItem> CreateDefaults() =>
    [
        new UserMenuItem { Title = "Open Explorer here",  Command = "explorer \"{panelDir}\"" },
        new UserMenuItem { Title = "List directory",       Command = "dir \"{panelDir}\"" },
        new UserMenuItem { Title = "Copy path to clipboard", Command = "echo {current}| clip" },
    ];
}
