using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using CSharpFar.Console;
using CSharpFar.Console.Ansi;

namespace CSharpFar.Architecture.Tests;

public sealed class ReusableTestDependencyTests
{
    [Theory]
    [InlineData("CSharpFar.Console.Tests", "src/CSharpFar.Console")]
    [InlineData("CSharpFar.Console.Ansi.Tests", "src/CSharpFar.Console", "src/CSharpFar.Console.Ansi")]
    [InlineData("CSharpFar.Console.Windows.Tests", "src/CSharpFar.Console", "src/CSharpFar.Console.Windows")]
    [InlineData("CSharpFar.Ui.Tests", "src/CSharpFar.Console", "src/CSharpFar.Ui", "tests/CSharpFar.Testing")]
    [InlineData("CSharpFar.Testing", "src/CSharpFar.Console")]
    [InlineData("CSharpFar.Architecture.Tests", "samples/CSharpFar.Ui.Demo", "src/CSharpFar.Console", "src/CSharpFar.Console.Ansi", "src/CSharpFar.Console.Windows", "src/CSharpFar.Ui")]
    public void ProjectReferences_MatchTheIsolatedBoundary(string projectName, params string[] allowed)
    {
        string root = RepositoryPaths.FindRoot();
        string projectDirectory = Path.Combine(root, "tests", projectName);
        XDocument project = XDocument.Load(Path.Combine(projectDirectory, projectName + ".csproj"));
        string[] references = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFullPath(Path.Combine(projectDirectory,
                reference.Attribute("Include")!.Value.Replace('\\', '/'))))
            .Order(StringComparer.Ordinal).ToArray();
        string[] expected = allowed.Select(path => Path.GetFullPath(Path.Combine(root, path,
                path.Split('/')[^1] + ".csproj")))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, references);
        Assert.Empty(project.Descendants("Reference"));

        if (projectName == "CSharpFar.Architecture.Tests")
        {
            XElement demo = Assert.Single(project.Descendants("ProjectReference"), reference =>
                reference.Attribute("Include")!.Value.Contains("CSharpFar.Ui.Demo"));
            Assert.Equal("false", demo.Attribute("ReferenceOutputAssembly")?.Value);
        }
    }

    [Fact]
    public void ReusableFriends_AreExplicitAndLimited()
    {
        AssertFriends(typeof(IConsoleDriver).Assembly,
            "CSharpFar.Console.Ansi", "CSharpFar.Console.Ansi.Tests", "CSharpFar.Console.Tests", "CSharpFar.Console.Windows");
        AssertFriends(typeof(AnsiTerminalConsoleDriver).Assembly, "CSharpFar.Console.Ansi.Tests");
        AssertFriends(typeof(SystemConsoleDriver).Assembly, "CSharpFar.Console.Windows.Tests");
        AssertFriends(typeof(FormControls).Assembly, "CSharpFar.Ui.Tests");
    }

    private static void AssertFriends(Assembly assembly, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .Order(StringComparer.Ordinal));

}
