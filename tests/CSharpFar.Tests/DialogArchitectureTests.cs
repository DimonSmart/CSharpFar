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

    [Fact]
    public void DropdownSelectInput_UsesCommittedFrameBeforeOpenStateBranching()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "DropdownSelect.cs"));

        Assert.Contains("bool IsOpen", source, StringComparison.Ordinal);
        Assert.Contains("int SelectionBeforeOpen", source, StringComparison.Ordinal);
        Assert.Contains("RestoreCommittedFrame(frame);", source, StringComparison.Ordinal);

        Assert.DoesNotContain("if (!IsOpen)", MethodBody(source, "TryHandleKey"), StringComparison.Ordinal);
        Assert.DoesNotContain("if (IsOpen)", MethodBody(source, "TryHandleKey"), StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsOpen)", MethodBody(source, "TryHandlePopupMouse"), StringComparison.Ordinal);
        Assert.DoesNotContain("if (IsOpen)", MethodBody(source, "TryHandlePopupMouse"), StringComparison.Ordinal);

        string keyBody = MethodBody(source, "TryHandleKey");
        Assert.True(
            keyBody.IndexOf("RestoreCommittedFrame(frame);", StringComparison.Ordinal) <
            keyBody.IndexOf("if (!frame.IsOpen)", StringComparison.Ordinal));
    }

    [Fact]
    public void ScrollableFormRouteInput_RestoresCommittedDropdownStateBeforeRouteBranching()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "ScrollableFormDialog.cs"));
        string routeInputBody = MethodBody(source, "RouteInput");
        string outsideClickBody = MethodBody(source, "private static bool CloseFocusedDropdownOnOutsideClick");

        Assert.True(
            routeInputBody.IndexOf("RestoreCommittedComponentState(frame);", StringComparison.Ordinal) <
            routeInputBody.IndexOf("RouteKey(key, frame, route)", StringComparison.Ordinal));
        Assert.True(
            routeInputBody.IndexOf("RestoreCommittedComponentState(frame);", StringComparison.Ordinal) <
            routeInputBody.IndexOf("RouteMouse(mouse, frame, route)", StringComparison.Ordinal));
        Assert.DoesNotContain("IsDropdownOpen", outsideClickBody, StringComparison.Ordinal);
        Assert.Contains("DropdownFrame: { IsOpen: true }", outsideClickBody, StringComparison.Ordinal);
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

    private static string MethodBody(string source, string methodName)
    {
        int nameIndex = source.IndexOf(methodName, StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Method '{methodName}' was not found.");
        int openBrace = source.IndexOf('{', nameIndex);
        Assert.True(openBrace >= 0, $"Method '{methodName}' has no body.");

        int depth = 0;
        for (int index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[openBrace..(index + 1)];
            }
        }

        throw new InvalidOperationException($"Method '{methodName}' body was not closed.");
    }
}
