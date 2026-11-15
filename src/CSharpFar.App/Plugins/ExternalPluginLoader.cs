using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CSharpFar.Core.Abstractions;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal sealed record ExternalPluginDiscovery(
    IReadOnlyList<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> Plugins,
    IReadOnlyList<string> Errors,
    ExternalPluginManager Manager);

/// <summary>Only manifested extensions are loaded, after metadata and enablement checks.</summary>
internal static class ExternalPluginLoader
{
    public static ExternalPluginDiscovery Discover(string configDirectory, bool noExtensions = false) =>
        new ExternalPluginManager(configDirectory, noExtensions).Discover();

    internal static (PluginDescriptor Descriptor, ICSharpFarPlugin Plugin) Load(ExternalPluginEntry entry)
    {
        entry.FailureStage = "load";
        string assemblyPath = Path.Combine(entry.InstallationPath, entry.AssemblyName!);
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Extension assembly '{entry.AssemblyName}' was not found.");

        var context = new ExtensionLoadContext(assemblyPath);
        Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        Type type = assembly.GetType(entry.EntryType!, throwOnError: false)
            ?? throw new TypeLoadException($"Entry type '{entry.EntryType}' was not found.");
        if (!type.IsClass || type.IsAbstract || !type.IsPublic ||
            !typeof(ICSharpFarPlugin).IsAssignableFrom(type))
            throw new InvalidDataException($"Entry type '{entry.EntryType}' must be a public ICSharpFarPlugin class.");
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new MissingMethodException($"Entry type '{entry.EntryType}' needs a public parameterless constructor.");

        entry.FailureStage = "construction";
        var plugin = (ICSharpFarPlugin)Activator.CreateInstance(type)!;
        return (new PluginDescriptor { Id = entry.Id!, Title = entry.Name!, Version = entry.Version }, plugin);
    }

    internal static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Manifest field '{property}' must be a nonempty string.");
        return value.GetString()!;
    }

    internal static bool IsSafeAssemblyFileName(string filename) =>
        filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
        filename.Length > 4 &&
        filename.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.') &&
        !Path.IsPathRooted(filename) &&
        Path.GetFileName(filename) == filename;

    private sealed class ExtensionLoadContext(string mainAssemblyPath)
        : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);
        private readonly string _directory = Path.GetDirectoryName(mainAssemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ICSharpFarPlugin).Assembly.GetName().Name)
                return typeof(ICSharpFarPlugin).Assembly;
            if (assemblyName.Name == typeof(IFilePanelSource).Assembly.GetName().Name)
                return typeof(IFilePanelSource).Assembly;

            string? path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (path is null && assemblyName.Name is { } name)
            {
                string adjacent = Path.Combine(_directory, name + ".dll");
                if (File.Exists(adjacent))
                    path = adjacent;
            }
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
