namespace CSharpFar.Tests;

public sealed class UiArchitectureInvariantTests
{
    [Fact]
    public void FocusCommit_IsOwnedOnlyByUiLayerInfrastructure()
    {
        string uiRoot = SourcePath("src", "CSharpFar.Ui");
        string[] callers = Directory.GetFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("FocusScope.Commit", StringComparison.Ordinal) ||
                File.ReadAllText(path).Contains("focusScope.Commit", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .ToArray();

        Assert.Equal(["UiLayer.cs"], callers);
    }

    [Fact]
    public void ScrollableForm_HasNoCompatibilityFocusScope()
    {
        string uiRoot = SourcePath("src", "CSharpFar.Ui");
        string source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(uiRoot, "ScrollableForm*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("_compatFocusScope", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new UiFocusScope", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FormInputResult HandleKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FormInputResult HandleMouse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bool TryFocus", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MigratedLibraryDialogs_UseInteractiveLifecycleAndCursorMetadata()
    {
        string[] files =
        [
            SourcePath("src", "CSharpFar.Ui", "MessageDialog.cs"),
            SourcePath("src", "CSharpFar.Ui", "SingleLineInputDialog.cs"),
            SourcePath("src", "CSharpFar.Ui", "ModuleHelpDialog.cs"),
        ];

        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            Assert.Contains("RunInteractive", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetCursorVisible(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetCursorPosition(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_modalDialogs.Run(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ModalConvenienceApis_DelegateToOneRunnerLoop()
    {
        string source = File.ReadAllText(SourcePath("src", "CSharpFar.Ui", "ModalDialogHost.cs"));

        Assert.Contains("RunInteractiveCore<TFrame, Unit, TResult>", source, StringComparison.Ordinal);
        Assert.Equal(1, source.Split("while (true)", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ApplicationInteractiveUi_DoesNotControlCursorOrReadConsoleDirectly()
    {
        string appRoot = SourcePath("src", "CSharpFar.App");
        string[] areas = ["Dialogs", "Viewer", "Editor"];
        string[] forbidden = ["SetCursorVisible(", "SetCursorPosition(", ".ReadInput(", ".TryReadInput("];

        foreach (string area in areas)
            foreach (string file in Directory.GetFiles(Path.Combine(appRoot, area), "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (string value in forbidden)
                    Assert.DoesNotContain(value, source, StringComparison.Ordinal);
            }
    }

    [Fact]
    public void InteractiveFullScreenFeatures_DoNotUseRenderOnlyLayers()
    {
        string[] files =
        [
            SourcePath("src", "CSharpFar.App", "Viewer", "HelpViewer.cs"),
            SourcePath("src", "CSharpFar.App", "Viewer", "LargeFileViewer.cs"),
            SourcePath("src", "CSharpFar.App", "Editor", "FileEditor.cs"),
        ];

        foreach (string file in files)
            Assert.DoesNotContain("RenderOnly", File.ReadAllText(file), StringComparison.Ordinal);
    }

    private static string SourcePath(params string[] parts)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }
}
