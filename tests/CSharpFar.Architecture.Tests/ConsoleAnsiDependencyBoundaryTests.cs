using System.Xml.Linq;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Architecture.Tests;

public sealed class ConsoleAnsiDependencyBoundaryTests
{
    [Fact]
    public void AnsiBackend_DependsOnlyOnReusableConsoleAndRuntimeAssemblies()
    {
        string projectPath = FindRepositoryFile(
            "src", "CSharpFar.Console.Ansi", "CSharpFar.Console.Ansi.csproj");
        XDocument project = XDocument.Load(projectPath);

        string[] projectReferences = project.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => include is not null)
            .Select(include => include!
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.Equal(["CSharpFar.Console"], projectReferences);
        Assert.Empty(project.Descendants("PackageReference"));

        string[] productAssemblyReferences = typeof(AnsiTerminalConsoleDriver).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("CSharpFar.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(["CSharpFar.Console"], productAssemblyReferences);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("The repository ANSI console project was not found.");
    }
}
