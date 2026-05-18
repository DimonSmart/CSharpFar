using System.Reflection;
using FarNet;

namespace CSharpFar.FarNetHost;

internal static class FarNetAssemblyCompatibility
{
    public static Version SupportedVersion { get; } =
        typeof(Far).Assembly.GetName().Version ?? new Version(10, 0, 30, 0);

    public static bool IsSupported(AssemblyName assemblyName) =>
        !string.Equals(assemblyName.Name, "FarNet", StringComparison.OrdinalIgnoreCase) ||
        assemblyName.Version is null ||
        assemblyName.Version <= SupportedVersion;

    public static bool IsFrameworkAssembly(AssemblyName assemblyName) =>
        assemblyName.Name is not null &&
        (assemblyName.Name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
         assemblyName.Name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
         assemblyName.Name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
         assemblyName.Name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
}
