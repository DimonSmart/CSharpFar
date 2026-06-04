using System.Text.Json;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Settings;

/// <summary>
/// Loads and saves application settings from/to settings.json.
/// Supports portable mode: if a file named "CSharpFar.portable" exists next
/// to the executable, config files go to CSharpFar.config/ beside the exe.
/// Otherwise config files go to %APPDATA%\CSharpFar\.
/// Creates a default settings.json on first run.
/// </summary>
public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public string      ConfigDirectory { get; }
    public AppSettings Settings        { get; }

    public JsonSettingsStore(string configDirectory)
    {
        ConfigDirectory = configDirectory;
        _filePath = Path.Combine(configDirectory, "settings.json");
        Settings  = Load();
    }

    /// <summary>
    /// Creates a store using the resolved config directory (portable or AppData).
    /// </summary>
    public static JsonSettingsStore Create(string? exePath = null)
    {
        string resolved = exePath
            ?? Environment.ProcessPath
            ?? AppContext.BaseDirectory;
        string exeDir = Path.GetDirectoryName(resolved) ?? ".";
        return new JsonSettingsStore(ResolveConfigDirectory(exeDir));
    }

    /// <summary>
    /// Returns the config directory for a given exe directory.
    /// Portable mode activates when CSharpFar.portable exists next to the exe.
    /// </summary>
    public static string ResolveConfigDirectory(string exeDir)
    {
        if (File.Exists(Path.Combine(exeDir, "CSharpFar.portable")))
            return Path.Combine(exeDir, "CSharpFar.config");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CSharpFar");
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch { /* best effort — never crash */ }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            var defaults = new AppSettings();
            WriteFile(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("Settings file does not contain a JSON object: " + _filePath);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Settings file is invalid: " + _filePath, ex);
        }
    }

    private void WriteFile(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch { /* best effort */ }
    }
}
