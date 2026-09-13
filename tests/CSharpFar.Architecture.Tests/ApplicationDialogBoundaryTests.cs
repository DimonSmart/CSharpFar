namespace CSharpFar.Architecture.Tests;

public sealed class ApplicationDialogBoundaryTests
{
    private static readonly string[] ForbiddenUiInfrastructure =
    [
        "ScrollableFormDialog",
        "CompositeDialogHost",
        "ICompositeDialogContent",
        "CompositeDialogEvent",
    ];

    [Fact]
    public void ApplicationAndModuleDialogs_DoNotDependOnLowLevelModalInfrastructure()
    {
        string root = FindRepositoryRoot();
        IEnumerable<string> applicationDialogs = Directory.EnumerateFiles(
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs"),
            "*.cs",
            SearchOption.AllDirectories);
        IEnumerable<string> moduleDialogs = Directory
            .EnumerateDirectories(Path.Combine(root, "src"), "CSharpFar.Module.*", SearchOption.TopDirectoryOnly)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*Dialog.cs", SearchOption.AllDirectories));

        string[] violations = applicationDialogs
            .Concat(moduleDialogs)
            .SelectMany(file =>
            {
                string source = File.ReadAllText(file);
                return ForbiddenUiInfrastructure
                    .Where(typeName => source.Contains(typeName, StringComparison.Ordinal))
                    .Select(typeName => $"{Path.GetRelativePath(root, file)}: {typeName}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Application-facing dialogs must describe UI semantics; CSharpFar.Ui owns layout, rendering, focus, input routing and modal lifecycle."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ProcessesAndPortsDialog_UsesOnlySemanticDynamicTableApi()
    {
        string file = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CSharpFar.Module.ProcessesAndPorts",
            "ProcessesAndPortsDialog.cs");
        string source = File.ReadAllText(file);
        string[] forbidden =
        [
            "ScrollableFormDialog",
            "TableList<",
            ".Composite<",
            "CompositeDialogEvent",
            "ReplaceItems",
            "SetButtons",
        ];

        string[] violations = forbidden
            .Where(source.Contains)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "ProcessesAndPortsDialog must use the semantic dynamic-table boundary: "
            + string.Join(", ", violations));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "CSharpFar.App")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
