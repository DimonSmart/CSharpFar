using System.Reflection;

namespace CSharpFar.App.Updates;

public static class ApplicationVersionProvider
{
    public static string GetDisplayVersion(Assembly? assembly = null) =>
        GetVersionInfo(assembly).DisplayVersion;

    internal static ApplicationVersionInfo GetVersionInfo(Assembly? assembly = null)
    {
        assembly ??= Assembly.GetEntryAssembly() ?? typeof(ApplicationVersionProvider).Assembly;
        string? informationalVersion =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return GetVersionInfo(informationalVersion, assembly.GetName().Version);
    }

    internal static ApplicationVersionInfo GetVersionInfo(
        string? informationalVersion,
        Version? assemblyVersion)
    {
        string? normalizedInformational = string.IsNullOrWhiteSpace(informationalVersion)
            ? null
            : informationalVersion.Trim();
        string displayVersion = normalizedInformational
            ?? assemblyVersion?.ToString()
            ?? "unknown";

        ReleaseVersion? comparable = null;
        if (ReleaseVersionParser.TryParse(normalizedInformational, out ReleaseVersion fromInformational))
            comparable = fromInformational;
        else if (ReleaseVersionParser.TryParse(assemblyVersion, out ReleaseVersion fromAssembly))
            comparable = fromAssembly;

        return new ApplicationVersionInfo(
            displayVersion,
            comparable,
            normalizedInformational,
            assemblyVersion);
    }
}

internal sealed record ApplicationVersionInfo(
    string DisplayVersion,
    ReleaseVersion? ComparableVersion,
    string? InformationalVersion = null,
    Version? AssemblyVersion = null)
{
    public string AboutVersion => ComparableVersion?.ToString() ?? DisplayVersion;
}
