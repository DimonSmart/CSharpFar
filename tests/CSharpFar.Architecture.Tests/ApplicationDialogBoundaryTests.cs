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
    public void ApplicationDialogs_DoNotDependOnLowLevelModalInfrastructure()
    {
        string dialogsDirectory = FindRepositoryDirectory("src", "CSharpFar.App", "Dialogs");

        string[] violations = Directory
            .EnumerateFiles(dialogsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(file =>
            {
                string source = File.ReadAllText(file);
                return ForbiddenUiInfrastructure
                    .Where(typeName => source.Contains(typeName, StringComparison.Ordinal))
                    .Select(typeName => $"{Path.GetFileName(file)}: {typeName}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Application dialogs must describe UI semantics; CSharpFar.Ui owns layout, rendering, focus, input routing and modal lifecycle."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryDirectory(params string[] parts)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine([directory.FullName, .. parts]);
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("The application dialogs directory was not found.");
    }
}
