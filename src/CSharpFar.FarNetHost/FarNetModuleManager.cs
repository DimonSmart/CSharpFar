using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class FarNetModuleManager : IModuleManager
{
    private readonly string _dataRoot;
    private readonly Dictionary<Guid, IModuleAction> _actions;
    private readonly Action<string, string> _addDiagnostic;
    private readonly HashSet<string> _reportedResourceProblems = new(StringComparer.OrdinalIgnoreCase);

    public FarNetModuleManager(
        string moduleName,
        Assembly assembly,
        string dataRoot,
        Dictionary<Guid, IModuleAction> actions,
        Action<string, string> addDiagnostic)
    {
        ModuleName = moduleName;
        Assembly = assembly;
        _dataRoot = dataRoot;
        _actions = actions;
        _addDiagnostic = addDiagnostic;
    }

    public Assembly Assembly { get; }

    public override CultureInfo CurrentUICulture { get; set; } = CultureInfo.CurrentUICulture;

    public override string ModuleName { get; }

    public override string StoredUICulture { get; set; } = CultureInfo.CurrentUICulture.Name;

    public override string? GetString(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        bool sawResourceFile = false;
        foreach (string resourcePath in GetResourcePaths())
        {
            if (!File.Exists(resourcePath))
                continue;

            sawResourceFile = true;
            string? value = TryReadResourceString(resourcePath, name);
            if (value is not null)
                return value;
        }

        if (!sawResourceFile)
            AddResourceDiagnostic("resource-missing-" + name, $"Resource file for key '{name}' was not found.");
        else
            AddResourceDiagnostic("resource-key-" + name, $"Resource key '{name}' was not found.");

        return name;
    }

    public override string GetFolderPath(SpecialFolder folder, bool create)
    {
        string folderName = folder switch
        {
            SpecialFolder.LocalData => "local",
            SpecialFolder.RoamingData => "roaming",
            _ => folder.ToString(),
        };
        string path = Path.Combine(_dataRoot, folderName, ModuleName);
        if (create)
            Directory.CreateDirectory(path);
        return path;
    }

    public override void Unregister()
    {
        foreach (Guid actionId in _actions
            .Where(pair => ReferenceEquals(pair.Value.Manager, this))
            .Select(pair => pair.Key)
            .ToArray())
        {
            _actions.Remove(actionId);
        }
    }

    public override IModuleCommand RegisterCommand(
        ModuleCommandAttribute attribute,
        EventHandler<ModuleCommandEventArgs> handler) =>
        throw new FarNetUnsupportedApiException(nameof(RegisterCommand));

    public override IModuleDrawer RegisterDrawer(
        ModuleDrawerAttribute attribute,
        Action<IEditor, ModuleDrawerEventArgs> handler) =>
        throw new FarNetUnsupportedApiException(nameof(RegisterDrawer));

    public override IModuleTool RegisterTool(
        ModuleToolAttribute attribute,
        EventHandler<ModuleToolEventArgs> handler) =>
        throw new FarNetUnsupportedApiException(nameof(RegisterTool));

    public override Assembly LoadAssembly(bool connect) => Assembly;

    public override void SaveConfig()
    {
    }

    public override object? Interop(string command, object? args) =>
        throw new FarNetUnsupportedApiException(nameof(Interop));

    private IEnumerable<string> GetResourcePaths()
    {
        string moduleDirectory = Path.GetDirectoryName(Assembly.Location) ?? string.Empty;
        string assemblyName = Path.GetFileNameWithoutExtension(Assembly.Location);
        var culture = CurrentUICulture;
        while (!string.IsNullOrWhiteSpace(culture.Name))
        {
            yield return Path.Combine(moduleDirectory, assemblyName + "." + culture.Name + ".resources");
            culture = culture.Parent;
        }

        yield return Path.Combine(moduleDirectory, assemblyName + ".resources");
    }

    private string? TryReadResourceString(string resourcePath, string name)
    {
        try
        {
            using var reader = new ResourceReader(resourcePath);
            foreach (DictionaryEntry entry in reader)
            {
                if (entry.Key is string key &&
                    string.Equals(key, name, StringComparison.Ordinal) &&
                    entry.Value is string value)
                {
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or IOException)
        {
            AddResourceDiagnostic(
                "resource-invalid-" + resourcePath,
                $"Resource file '{resourcePath}' is invalid: {ex.Message}");
        }

        return null;
    }

    private void AddResourceDiagnostic(string key, string message)
    {
        if (_reportedResourceProblems.Add(key))
            _addDiagnostic(ModuleName, message);
    }
}
