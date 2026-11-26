using System.Text.RegularExpressions;

namespace CSharpFar.Architecture.Tests;

public sealed class ApplicationCommandBoundaryTests
{
    [Fact]
    public void Commands_DoNotUseUniversalContextOrServiceLocator()
    {
        string root = RepositoryPaths.FindRoot();
        string app = Path.Combine(root, "src", "CSharpFar.App");
        var files = Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")).ToArray();
        Assert.DoesNotContain(files, file => Regex.IsMatch(File.ReadAllText(file), @"\bApplicationCommandContext\b"));
        string commands = Path.Combine(app, "Commands");
        string[] forbidden = ["ApplicationServiceCallbacks", "ApplicationServices", "IServiceProvider",
            "ApplicationServicesBuilder", "CommandServicesFactory", "ApplicationFactory"];
        var violations = Directory.EnumerateFiles(commands, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => forbidden.Where(name => Regex.IsMatch(File.ReadAllText(file), $@"\b{Regex.Escape(name)}\b"))
                .Select(name => $"{Path.GetFileName(file)}: {name}")).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        foreach (string file in Directory.EnumerateFiles(commands, "*.cs"))
            Assert.DoesNotMatch(@"\bApplication\s+[a-zA-Z_]\w*\s*[,);{]", File.ReadAllText(file));
    }
}
