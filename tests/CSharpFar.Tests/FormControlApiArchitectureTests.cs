using System.Text.RegularExpressions;

namespace CSharpFar.Tests;

public sealed class FormControlApiArchitectureTests
{
    [Fact]
    public void ApplicationProjects_DoNotConstructLowLevelFormComponents()
    {
        string repositoryRoot = FindRepositoryRoot();
        string uiProject = Path.GetFullPath(Path.Combine(repositoryRoot, "src", "CSharpFar.Ui"));
        string[] forbiddenTypes = ["CheckBoxLine", "TriStateCheckBoxLine", "ChoiceModel", "DropdownSelect", "DialogButtonBar"];

        string[] violations = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetFullPath(file).StartsWith(uiProject + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => forbiddenTypes
                .Where(type => Regex.IsMatch(File.ReadAllText(file), $@"\bnew\s+{type}(?:\s*<[^>]+>)?\s*\("))
                .Select(type => $"{Path.GetRelativePath(repositoryRoot, file)}: {type}"))
            .ToArray();

        Assert.True(violations.Length == 0, "Low-level UI construction found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
