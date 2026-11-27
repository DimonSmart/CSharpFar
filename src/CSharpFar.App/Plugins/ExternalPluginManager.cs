using System.Reflection;
using System.Text.Json;
using CSharpFar.App.Diagnostics;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal enum ExternalPluginStatus { Loaded, Disabled, Error }

internal sealed class ExternalPluginEntry(string folderName, string installationPath, bool enabled)
{
    public string FolderName { get; } = folderName;
    public string InstallationPath { get; } = installationPath;
    public string? Id { get; internal set; }
    public string? Name { get; internal set; }
    public string? Version { get; internal set; }
    public int? ApiVersion { get; internal set; }
    internal string? AssemblyName { get; set; }
    internal string? EntryType { get; set; }
    internal PluginDescriptor? Descriptor { get; set; }
    public bool Enabled { get; internal set; } = enabled;
    public bool EnabledAtStartup { get; } = enabled;
    public bool RequiresRestart => Enabled != EnabledAtStartup;
    public ExternalPluginStatus Status { get; internal set; } = ExternalPluginStatus.Disabled;
    public string? FailureStage { get; internal set; }
    public string? Error { get; internal set; }
    public string? DiagnosticDetails { get; internal set; }
}

/// <summary>
/// Installed extension metadata and the user's persisted enable/disable choices.
/// This is not a command registry: NativePluginRuntime owns all live registrations.
/// </summary>
internal sealed class ExternalPluginManager
{
    private readonly string _configDirectory;
    private readonly string _statePath;
    private readonly bool _noExtensions;
    private readonly string? _developmentRoot;
    private readonly List<ExternalPluginEntry> _entries = [];
    private readonly List<string> _errors = [];
    private HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private bool _stateUnavailable;
    private bool _externalPluginsEnabled = true;

    public ExternalPluginManager(string configDirectory, bool noExtensions = false, string? developmentRoot = null)
    {
        _configDirectory = configDirectory;
        _statePath = Path.Combine(configDirectory, "extensions-state.json");
        _noExtensions = noExtensions;
        _developmentRoot = developmentRoot;
        try { ReadState(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _stateUnavailable = true;
            _errors.Add("Extension configuration: " + Clean(error.Message));
        }
        ExternalPluginsEnabledAtStartup = _externalPluginsEnabled;
    }

    public IReadOnlyList<ExternalPluginEntry> Entries => _entries;
    public bool NoExtensions => _noExtensions;
    public bool ExternalPluginsEnabled => _externalPluginsEnabled;
    public bool ExternalPluginsEnabledAtStartup { get; }
    public bool GlobalRestartRequired => ExternalPluginsEnabled != ExternalPluginsEnabledAtStartup;
    public bool WillLoad(ExternalPluginEntry entry) => ExternalPluginsEnabled && entry.Enabled;
    public IReadOnlyList<string> Errors => _errors;

    public ExternalPluginDiscovery Discover()
    {
        var plugins = new List<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)>();
        string root = Path.Combine(_configDirectory, "extensions");
        // User-installed packages take precedence over development copies with the same folder name.
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddDirectories(root, directories);
        if (_developmentRoot is not null)
            AddDirectories(_developmentRoot, directories);

        foreach (string directory in directories.Values.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(Path.GetFileName, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(directory, "plugin.json")))
                continue;

            string folder = Path.GetFileName(directory);
            var entry = new ExternalPluginEntry(folder, directory, !_disabled.Contains(folder));
            _entries.Add(entry);

            try { ReadManifest(entry); }
            catch (Exception error)
            {
                Fail(entry, "manifest", error, reportAtStartup: WillLoad(entry) && !_noExtensions && !_stateUnavailable);
            }

            // Even malformed manifests must be manageable by folder name.
            if (!entry.Enabled)
            {
                entry.Status = ExternalPluginStatus.Disabled;
                continue;
            }

            if (_noExtensions || _stateUnavailable || !ExternalPluginsEnabledAtStartup)
            {
                if (entry.Error is null)
                    entry.Status = ExternalPluginStatus.Disabled;
                continue;
            }

            if (entry.Error is not null)
                continue;

            try
            {
                var plugin = ExternalPluginLoader.Load(entry);
                entry.Descriptor = plugin.Descriptor;
                entry.FailureStage = "initialization";
                plugins.Add(plugin);
            }
            catch (Exception error)
            {
                Fail(entry, entry.FailureStage ?? "load", error, reportAtStartup: true);
            }
        }
        return new(plugins, _errors, this);
    }

    private void AddDirectories(string root, Dictionary<string, string> directories)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (string directory in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.Ordinal))
                if (File.Exists(Path.Combine(directory, "plugin.json")))
                    directories.TryAdd(Path.GetFileName(directory), directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _errors.Add("Cannot enumerate extensions: " + Clean(error.Message));
        }
    }

    public void Complete(NativePluginRuntime? runtime, IReadOnlyList<string> startupErrors)
    {
        var registered = runtime?.RegisteredDescriptors ?? [];
        foreach (ExternalPluginEntry entry in _entries)
        {
            if (entry.Descriptor is null)
                continue;

            if (registered.Any(descriptor => ReferenceEquals(descriptor, entry.Descriptor)))
            {
                entry.Status = ExternalPluginStatus.Loaded;
                entry.FailureStage = null;
                continue;
            }

            entry.Status = ExternalPluginStatus.Error;
            entry.FailureStage = "initialization";
            string prefix = $"Plugin '{entry.Id}':";
            entry.Error = startupErrors.FirstOrDefault(message =>
                message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ?? "Plugin registration or initialization failed.";
        }
    }

    public bool TrySetEnabled(ExternalPluginEntry entry, bool enabled, out string? error)
    {
        if (!_entries.Contains(entry))
        {
            error = "Unknown extension.";
            return false;
        }

        if (entry.Enabled == enabled)
        {
            error = null;
            return true;
        }

        var updated = new HashSet<string>(_disabled, StringComparer.OrdinalIgnoreCase);
        if (enabled) updated.Remove(entry.FolderName);
        else updated.Add(entry.FolderName);

        if (!TrySaveState(updated, _externalPluginsEnabled, out error))
            return false;

        _disabled = updated;
        entry.Enabled = enabled;
        return true;
    }

    public bool TrySetExternalPluginsEnabled(bool enabled, out string? error)
    {
        if (enabled == _externalPluginsEnabled)
        {
            error = null;
            return true;
        }

        if (!TrySaveState(_disabled, enabled, out error))
            return false;

        _externalPluginsEnabled = enabled;
        return true;
    }

    private bool TrySaveState(HashSet<string> updated, bool enabled, out string? error)
    {
        if (_stateUnavailable)
        {
            error = "Extension configuration is unavailable; repair extensions-state.json first.";
            return false;
        }

        string temporary = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_configDirectory);
            string content = JsonSerializer.Serialize(new
            {
                enabled,
                disabled = updated.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporary, content);
            File.Move(temporary, _statePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = Clean(exception.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        error = null;
        return true;
    }

    private void ReadState()
    {
        if (!File.Exists(_statePath)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(_statePath));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("disabled", out var disabled) ||
            disabled.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("extensions-state.json must contain a disabled array.");

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement value in disabled.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String ||
                value.GetString() is not { Length: > 0 } folder ||
                folder is "." or ".." ||
                folder.Contains('/') || folder.Contains('\\'))
                throw new InvalidDataException("Invalid extension directory name in disabled list.");
            values.Add(folder);
        }
        if (document.RootElement.TryGetProperty("enabled", out var enabled))
        {
            if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("extensions-state.json enabled must be a boolean.");
            _externalPluginsEnabled = enabled.GetBoolean();
        }
        _disabled = values;
    }

    private static void ReadManifest(ExternalPluginEntry entry)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(entry.InstallationPath, "plugin.json")));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("plugin.json must contain a JSON object.");

        entry.Id = ExternalPluginLoader.RequiredString(root, "id");
        if (entry.Id is "." or ".." ||
            !entry.Id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new InvalidDataException("Invalid plugin ID: expected a safe single path component.");

        entry.Name = ExternalPluginLoader.RequiredString(root, "name");
        entry.Version = ExternalPluginLoader.RequiredString(root, "version");
        if (!root.TryGetProperty("apiVersion", out var apiVersion) ||
            apiVersion.ValueKind != JsonValueKind.Number || !apiVersion.TryGetInt32(out int api))
            throw new InvalidDataException("apiVersion must be an integer.");
        entry.ApiVersion = api;
        if (api != 2)
            throw new NotSupportedException($"Unsupported plugin API version {api}; expected 2; rebuild the plugin against SDK 2.x.");

        entry.AssemblyName = ExternalPluginLoader.RequiredString(root, "assembly");
        if (!ExternalPluginLoader.IsSafeAssemblyFileName(entry.AssemblyName))
            throw new InvalidDataException("assembly must be a DLL filename inside the extension directory.");
        entry.EntryType = ExternalPluginLoader.RequiredString(root, "entryType");
    }

    private void Fail(ExternalPluginEntry entry, string stage, Exception error, bool reportAtStartup)
    {
        Exception cause = (error as TargetInvocationException)?.InnerException ?? error;
        entry.Status = ExternalPluginStatus.Error;
        entry.FailureStage = stage;
        entry.Error = Clean(cause.Message);
        entry.DiagnosticDetails = Clean($"{cause.GetType().Name}: {cause.Message}");
        if (reportAtStartup)
            _errors.Add($"Extension '{entry.FolderName}' ({entry.Id ?? "unknown"}) [{stage}]: {entry.Error}");
    }

    private static string Clean(string? message) =>
        DiagnosticSanitizer.SanitizeText(message).Replace('\r', ' ').Replace('\n', ' ') is string sanitized
            ? sanitized.Length > 400 ? sanitized[..400] + "…" : sanitized
            : string.Empty;
}
