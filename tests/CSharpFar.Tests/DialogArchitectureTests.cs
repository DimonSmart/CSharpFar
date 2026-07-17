namespace CSharpFar.Tests;

public sealed class DialogArchitectureTests
{
    [Fact]
    public void MigratedFormDialogs_DoNotUseLegacyLocalModalMechanics()
    {
        string root = FindRepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "CreateFolderDialog.cs"),
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "DirectoryShortcutEditDialog.cs"),
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "OpenCreateFileDialog.cs"),
        ];
        string[] forbidden =
        [
            "focusRow",
            "focusedButton",
            "ScrollBarDragState",
            "DialogButtonBar",
            "SetCursorPosition",
            "SetCursorVisible",
        ];

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            foreach (string token in forbidden)
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CSharpFar.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
