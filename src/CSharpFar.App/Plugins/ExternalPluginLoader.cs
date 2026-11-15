using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CSharpFar.Core.Abstractions;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal sealed record ExternalPluginDiscovery(
    IReadOnlyList<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> Plugins,
    IReadOnlyList<string> Errors);

/// <summary>Discovers only explicitly manifested extensions, never arbitrary DLLs.</summary>
internal static class ExternalPluginLoader
{
    public static ExternalPluginDiscovery Discover(string configDirectory)
    {
        var plugins = new List<(PluginDescriptor, ICSharpFarPlugin)>();
        var errors = new List<string>();
        string extensionsDirectory = Path.Combine(configDirectory, "extensions");
        if (!Directory.Exists(extensionsDirectory))
            return new(plugins, errors);

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(extensionsDirectory);
            Array.Sort(directories, (left, right) =>
            {
                int comparison = StringComparer.OrdinalIgnoreCase.Compare(
                    Path.GetFileName(left), Path.GetFileName(right));
                return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot enumerate extensions: {e.Message}");
            return new(plugins, errors);
        }

        foreach (string directory in directories)
        {
            string folderName = Path.GetFileName(directory);
            try
            {
                string manifestFile = Path.Combine(directory, "plugin.json");
                if (!File.Exists(manifestFile))
                    continue;

                using var document = JsonDocument.Parse(File.ReadAllText(manifestFile));
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("plugin.json must contain a JSON object.");

                string id = RequiredString(root, "id");
                if (id is "." or ".." ||
                    !id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
                    throw new InvalidDataException("Invalid plugin ID: expected a safe single path component.");

                string title = RequiredString(root, "name");
                string version = RequiredString(root, "version");
                if (!root.TryGetProperty("apiVersion", out var apiVersion) ||
                    apiVersion.ValueKind != JsonValueKind.Number || !apiVersion.TryGetInt32(out int api))
                    throw new InvalidDataException("apiVersion must be an integer.");
                if (api != 1)
                    throw new NotSupportedException($"Unsupported plugin API version {api}; expected 1.");

                string assemblyName = RequiredString(root, "assembly");
                if (!IsSafeAssemblyFileName(assemblyName))
                    throw new InvalidDataException("assembly must be a DLL filename inside the extension directory.");
                string entryType = RequiredString(root, "entryType");
                string assemblyPath = Path.Combine(directory, assemblyName);
                if (!File.Exists(assemblyPath))
                    throw new FileNotFoundException($"Extension assembly '{assemblyName}' was not found.");

                var context = new ExtensionLoadContext(assemblyPath);
                Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
                Type type = assembly.GetType(entryType, throwOnError: false)
                    ?? throw new TypeLoadException($"Entry type '{entryType}' was not found.");
                if (!type.IsClass || type.IsAbstract || !type.IsPublic ||
                    !typeof(ICSharpFarPlugin).IsAssignableFrom(type))
                    throw new InvalidDataException($"Entry type '{entryType}' must be a public ICSharpFarPlugin class.");
                if (type.GetConstructor(Type.EmptyTypes) is null)
                    throw new MissingMethodException($"Entry type '{entryType}' needs a public parameterless constructor.");

                var plugin = (ICSharpFarPlugin)Activator.CreateInstance(type)!;
                plugins.Add((new PluginDescriptor { Id = id, Title = title, Version = version }, plugin));
            }
            catch (Exception error)
            {
                Exception cause = error is TargetInvocationException { InnerException: not null } invocation
                    ? invocation.InnerException : error;
                errors.Add($"Extension '{folderName}': {cause.Message}");
            }
        }

        return new(plugins, errors);
    }

    private static string RequiredString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Manifest field '{property}' must be a nonempty string.");
        return value.GetString()!;
    }

    private static bool IsSafeAssemblyFileName(string filename) =>
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
