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
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public string ConfigDirectory { get; }
    public AppSettings Settings { get; }
    private readonly Func<AppSettings> _createDefaultSettings;

    public JsonSettingsStore(string configDirectory, Func<AppSettings>? createDefaultSettings = null)
    {
        ConfigDirectory = configDirectory;
        _filePath = Path.Combine(configDirectory, "settings.json");
        _createDefaultSettings = createDefaultSettings ?? (() => new AppSettings());
        Settings = Load();
    }

    /// <summary>
    /// Creates a store using the resolved config directory (portable or AppData).
    /// </summary>
    public static JsonSettingsStore Create(string? exePath = null, Func<AppSettings>? createDefaultSettings = null)
    {
        string resolved = exePath
            ?? Environment.ProcessPath
            ?? AppContext.BaseDirectory;
        string exeDir = Path.GetDirectoryName(resolved) ?? ".";
        return new JsonSettingsStore(ResolveConfigDirectory(exeDir), createDefaultSettings);
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

    /// <summary>
    /// Reads only the Finder-launch preference from the existing settings.json.
    /// Does not create files or deserialize unrelated settings sections.
    /// </summary>
    public static string ReadMacOsTerminalPreference(string executableDirectory, Action<string>? reportError = null) =>
        ReadMacOsTerminalPreference(executableDirectory, File.ReadAllText, reportError);

    internal static string ReadMacOsTerminalPreference(
        string executableDirectory,
        Func<string, string> readFile,
        Action<string>? reportError = null)
    {
        try
        {
            string configDirectory = ResolveConfigDirectory(executableDirectory);
            string json = readFile(Path.Combine(configDirectory, "settings.json"));
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Settings root must be an object.");

            if (!root.TryGetProperty("application", out JsonElement application) ||
                application.ValueKind != JsonValueKind.Object ||
                !application.TryGetProperty("preferredMacOsTerminal", out JsonElement preference))
                return MacOsTerminalPreference.Auto;

            return MacOsTerminalPreference.Normalize(
                preference.ValueKind == JsonValueKind.String ? preference.GetString() : null);
        }
        catch (FileNotFoundException)
        {
            return MacOsTerminalPreference.Auto;
        }
        catch (DirectoryNotFoundException)
        {
            return MacOsTerminalPreference.Auto;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or
                                   NotSupportedException or JsonException)
        {
            reportError?.Invoke($"Cannot read launcher preference ({ex.GetType().Name}); using Terminal.app.");
            return MacOsTerminalPreference.Terminal;
        }
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
            var defaults = _createDefaultSettings();
            WriteFile(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? throw new InvalidDataException("Settings file does not contain a JSON object: " + _filePath);
            settings.Application ??= new AppSettings.ApplicationSettings();
            return settings;
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
