using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using CSharpFar.Console;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Architecture.Tests;

public sealed class ConsoleBackendArchitectureTests
{
    [Fact]
    public void ConsoleCore_IsPlatformNeutral()
    {
        Assembly core = typeof(IConsoleDriver).Assembly;

        Assert.DoesNotContain(core.GetTypes(), type =>
            type == typeof(SystemConsoleDriver) ||
            string.Equals(type.Namespace, "CSharpFar.Console.Win32", StringComparison.Ordinal) ||
            type.Namespace?.StartsWith("CSharpFar.Console.Win32.", StringComparison.Ordinal) == true);

        Assert.DoesNotContain(
            core.GetTypes().SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)),
            method => method.GetCustomAttribute<DllImportAttribute>() is not null);

        XDocument project = LoadProject("src", "CSharpFar.Console", "CSharpFar.Console.csproj");
        Assert.Empty(project.Descendants("ProjectReference"));
        AssertOnlyDevelopmentApiAnalyzer(project);
        Assert.DoesNotContain(project.Descendants("TargetFramework"), framework =>
            framework.Value.Contains("-windows", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("CSharpFar.Console.Windows")]
    [InlineData("CSharpFar.Console.Ansi")]
    public void Backends_DependOnlyOnConsole(string projectName)
    {
        XDocument project = LoadProject("src", projectName, projectName + ".csproj");
        Assert.Equal(["CSharpFar.Console"], ProjectReferenceNames(project));
        AssertOnlyDevelopmentApiAnalyzer(project);

        Assembly backend = projectName switch
        {
            "CSharpFar.Console.Windows" => typeof(SystemConsoleDriver).Assembly,
            "CSharpFar.Console.Ansi" => typeof(AnsiTerminalConsoleDriver).Assembly,
            _ => throw new ArgumentOutOfRangeException(nameof(projectName)),
        };
        Assert.Equal(["CSharpFar.Console"], ProductAssemblyReferences(backend));
    }

    [Fact]
    public void Backends_AreSiblingAssembliesAndDoNotDependOnEachOther()
    {
        Assembly core = typeof(IConsoleDriver).Assembly;
        Assembly windows = typeof(SystemConsoleDriver).Assembly;
        Assembly ansi = typeof(AnsiTerminalConsoleDriver).Assembly;

        Assert.NotSame(core, windows);
        Assert.NotSame(core, ansi);
        Assert.NotSame(windows, ansi);
        Assert.Equal("CSharpFar.Console.Windows", windows.GetName().Name);
        Assert.Empty(ProductAssemblyReferences(core));
    }

    [Fact]
    public void WindowsBackend_FailsFastOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        var error = Assert.Throws<PlatformNotSupportedException>(() => new SystemConsoleDriver());
        Assert.Contains("Windows", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ProjectReferenceNames(XDocument project) => project.Descendants("ProjectReference")
        .Select(reference => (string?)reference.Attribute("Include"))
        .Where(include => include is not null)
        .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/'))!)
        .ToArray();

    private static string[] ProductAssemblyReferences(Assembly assembly) => assembly.GetReferencedAssemblies()
        .Select(reference => reference.Name!)
        .Where(name => name.StartsWith("CSharpFar.", StringComparison.Ordinal))
        .ToArray();

    private static void AssertOnlyDevelopmentApiAnalyzer(XDocument project)
    {
        XElement reference = Assert.Single(project.Descendants("PackageReference"));
        Assert.Equal("Microsoft.CodeAnalysis.PublicApiAnalyzers", reference.Attribute("Include")?.Value);
        Assert.Equal("all", reference.Attribute("PrivateAssets")?.Value);
    }

    private static XDocument LoadProject(params string[] parts) => XDocument.Load(RepositoryPaths.FindFile(parts));

}
