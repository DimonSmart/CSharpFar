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
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "SettingsDialog.cs"),
            Path.Combine(root, "src", "CSharpFar.App", "Editor", "EditorFormatDialog.cs"),
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
    public void MigratedStandardDialogs_DoNotUseLegacyLocalInputEngines()
    {
        string root = FindRepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "SettingsDialog.cs"),
            Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "DriveDialog.cs"),
            Path.Combine(root, "src", "CSharpFar.App", "Editor", "EditorFormatDialog.cs"),
        ];
        string[] forbidden =
        [
            "focusRow",
            "focusedButton",
            "bodyScrollTop",
            "ScrollBarDragState",
            "SetCursorVisible",
            "SetCursorPosition",
            "TryHandleScrollbarMouse",
            "TryHandleListMouse",
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

    [Fact]
    public void MigratedStageSixDialogs_KeepTransactionalFixesExplicit()
    {
        string root = FindRepositoryRoot();
        string editor = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.App", "Editor", "EditorFormatDialog.cs"));
        string drive = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "DriveDialog.cs"));
        string settings = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.App", "Dialogs", "SettingsDialog.cs"));

        Assert.True(
            editor.IndexOf("FormInputResult.Submit()", StringComparison.Ordinal) <
            editor.IndexOf("form.RouteInput(input, frame, route)", StringComparison.Ordinal));
        Assert.Contains("list.ApplyCommittedFrame(frame.ListState)", drive, StringComparison.Ordinal);
        Assert.DoesNotContain("logicalSelectedIndex", drive, StringComparison.Ordinal);
        Assert.DoesNotContain("UiTheme.Initialize(PaletteRegistry.Resolve", settings, StringComparison.Ordinal);
        Assert.Contains("UiTheme.UseTemporary(PaletteRegistry.Resolve", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveSurfaceInfrastructure_UsesSharedCompositionPacketPump()
    {
        string root = FindRepositoryRoot();
        string pump = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "CompositionInputPump.cs"));
        string interactiveSurface = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "InteractiveSurfaceHost.cs"));
        string modal = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "ModalDialogHost.cs"));

        Assert.Contains("class CompositionInputPump", pump, StringComparison.Ordinal);
        Assert.Contains("ReadCompositionInput", pump, StringComparison.Ordinal);
        Assert.Contains("DispatchInput", pump, StringComparison.Ordinal);
        Assert.Contains("if (_tryTakePacket(out var packet))", pump, StringComparison.Ordinal);
        Assert.True(
            pump.IndexOf("if (_tryTakePacket(out var packet))", StringComparison.Ordinal) <
            pump.IndexOf("if (dispatch.Invalidate)", StringComparison.Ordinal));

        Assert.Contains("CompositionInputPump<InteractiveSurfaceInput", interactiveSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadInput(cancellationToken)", interactiveSurface, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatchInput(input)", interactiveSurface, StringComparison.Ordinal);
        Assert.Contains("CompositionInputPump<UiRoutedInput", modal, StringComparison.Ordinal);
        Assert.Contains("CompositionInputPump<InteractiveModalInput", modal, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpViewer_UsesInteractiveSurfaceLifecycleOnly()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.App", "Viewer", "HelpViewer.cs"));

        Assert.DoesNotContain(".OpenSurface(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".ReadInput(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Render(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (true)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCursorVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetCursorPosition", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InteractiveSurfaceWrapper_DoesNotHideRegisteredLayerIdentity()
    {
        string root = FindRepositoryRoot();
        string host = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "InteractiveSurfaceHost.cs"));
        string composition = File.ReadAllText(Path.Combine(root, "src", "CSharpFar.Ui", "UiCompositionHost.cs"));

        Assert.Contains("OpenSurface(new InteractiveSurface(_composition.Screen), layer)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IUiLayer", MethodBody(host, "internal sealed class InteractiveSurface"), StringComparison.Ordinal);
        Assert.Contains("OpenSurface(IUiSurface surfaceLifecycle, IUiLayer layer)", composition, StringComparison.Ordinal);
        Assert.Contains("EnsureLayerNotRegistered(layer);", MethodBody(composition, "OpenSurface(IUiSurface surfaceLifecycle, IUiLayer layer)"), StringComparison.Ordinal);
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
