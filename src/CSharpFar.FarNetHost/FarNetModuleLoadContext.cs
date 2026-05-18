using System.Reflection;
using System.Runtime.Loader;
using FarNet;

namespace CSharpFar.FarNetHost;

internal sealed class FarNetModuleLoadContext : AssemblyLoadContext
{
    private readonly string _moduleAssemblyPath;
    private readonly string _moduleDirectory;
    private readonly Assembly _farNetAssembly;
    private readonly AssemblyDependencyResolver _resolver;

    public FarNetModuleLoadContext(string moduleName, string moduleAssemblyPath)
        : base($"CSharpFar.FarNet.{moduleName}", isCollectible: true)
    {
        _moduleAssemblyPath = moduleAssemblyPath;
        _moduleDirectory = Path.GetDirectoryName(moduleAssemblyPath) ??
            throw new ArgumentException("Module assembly path must include a directory.", nameof(moduleAssemblyPath));
        _farNetAssembly = typeof(Far).Assembly;
        _resolver = new AssemblyDependencyResolver(moduleAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, "FarNet", StringComparison.OrdinalIgnoreCase))
        {
            if (!FarNetAssemblyCompatibility.IsSupported(assemblyName))
            {
                throw new FileLoadException(
                    $"FarNet module requests FarNet {assemblyName.Version}, but CSharpFar supports {FarNetAssemblyCompatibility.SupportedVersion}.",
                    _moduleAssemblyPath);
            }

            return _farNetAssembly;
        }

        string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolvedPath is not null &&
            !string.Equals(Path.GetFileName(resolvedPath), "FarNet.dll", StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromAssemblyPath(resolvedPath);
        }

        string localPath = Path.Combine(_moduleDirectory, assemblyName.Name + ".dll");
        if (File.Exists(localPath) &&
            !string.Equals(Path.GetFileName(localPath), "FarNet.dll", StringComparison.OrdinalIgnoreCase))
        {
            return LoadFromAssemblyPath(localPath);
        }

        if (!FarNetAssemblyCompatibility.IsFrameworkAssembly(assemblyName))
            throw new FileNotFoundException(
                $"FarNet module dependency '{assemblyName.Name}' was not found in '{_moduleDirectory}'.",
                assemblyName.Name + ".dll");

        return null;
    }
}
