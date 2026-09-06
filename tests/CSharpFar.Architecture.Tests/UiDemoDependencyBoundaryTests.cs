using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;


namespace CSharpFar.Architecture.Tests;

public sealed class UiDemoDependencyBoundaryTests
{
    [Fact]
    public void Demo_ProjectAndAssemblyClosure_StayIndependent()
    {
        string projectPath = FindRepositoryFile("samples", "CSharpFar.Ui.Demo", "CSharpFar.Ui.Demo.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] directProjects = ProjectReferences(projectPath, project).Select(path => Path.GetFileNameWithoutExtension(path)!).ToArray();
        Assert.Equal(["CSharpFar.Console", "CSharpFar.Console.Windows", "CSharpFar.Console.Ansi", "CSharpFar.Ui"], directProjects);
        Assert.All(project.Descendants("PackageReference"), reference =>
            Assert.Equal("'$(UseCSharpFarPackages)' == 'true'", reference.Parent?.Attribute("Condition")?.Value));

        string[] forbidden = ["CSharpFar.Core", "CSharpFar.App", "CSharpFar.FileSystem", "CSharpFar.Shell"];
        var closure = ProjectClosure(projectPath).Select(path => Path.GetFileNameWithoutExtension(path)!).ToArray();
        Assert.DoesNotContain(closure, name => forbidden.Contains(name) || name.StartsWith("CSharpFar.Platform.") || name.StartsWith("CSharpFar.Module."));

        string[] assemblyClosure = AssemblyClosure(Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "CSharpFar.Ui.Demo.dll"))).ToArray();
        Assert.DoesNotContain(assemblyClosure, name => forbidden.Contains(name) || name.StartsWith("CSharpFar.Platform.") || name.StartsWith("CSharpFar.Module."));
    }

    [Fact]
    public void Demo_GrantsNoFriendAssemblyAccess() =>
        Assert.Empty(Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "CSharpFar.Ui.Demo.dll")).GetCustomAttributes<InternalsVisibleToAttribute>());

    [Fact]
    public void Demo_PackageMode_IsMutuallyExclusiveWithProjectReferences()
    {
        XDocument project = XDocument.Load(FindRepositoryFile("samples", "CSharpFar.Ui.Demo", "CSharpFar.Ui.Demo.csproj"));
        Assert.All(project.Descendants("ProjectReference"), reference =>
            Assert.Equal("'$(UseCSharpFarPackages)' != 'true'", reference.Parent?.Attribute("Condition")?.Value));
        Assert.Equal(["CSharpFar.Console", "CSharpFar.Console.Ansi", "CSharpFar.Console.Windows", "CSharpFar.Ui"],
            project.Descendants("PackageReference").Select(reference => reference.Attribute("Include")!.Value).Order().ToArray());
    }

    private static IEnumerable<string> ProjectClosure(string root)
    {
        var pending = new Stack<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(root);
        while (pending.TryPop(out string? path))
        {
            foreach (string reference in ProjectReferences(path, XDocument.Load(path)))
            {
                string fullPath = Path.GetFullPath(reference);
                if (!seen.Add(fullPath)) continue;
                yield return fullPath;
                pending.Push(fullPath);
            }
        }
    }

    private static IEnumerable<string> AssemblyClosure(Assembly root)
    {
        var pending = new Stack<Assembly>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root);
        while (pending.TryPop(out Assembly? assembly))
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies().Where(value => value.Name!.StartsWith("CSharpFar.", StringComparison.Ordinal)))
            {
                string name = reference.Name!;
                if (!seen.Add(name)) continue;
                yield return name;
                pending.Push(Assembly.Load(reference));
            }
        }
    }

    private static IEnumerable<string> ProjectReferences(string projectPath, XDocument project) => project
        .Descendants("ProjectReference")
        .Select(reference => (string?)reference.Attribute("Include"))
        .Where(include => include is not null)
        .Select(include => include!
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar))
        .Select(include => Path.Combine(Path.GetDirectoryName(projectPath)!, include));

    private static string FindRepositoryFile(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("The repository demo project was not found.");
    }
}
