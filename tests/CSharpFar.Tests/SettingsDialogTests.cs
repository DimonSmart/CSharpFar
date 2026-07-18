using CSharpFar.App.Dialogs;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SettingsDialogTests
{
    [Fact]
    public void Show_ReturnsSelectedValuesOnF10()
    {
        var driver = Driver(
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.F10));

        SettingsDialogResult? result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            PanelViewMode.Full,
            PanelViewMode.Full,
            "Default",
            fileHighlightingEnabled: true,
            editorSyntaxHighlightingEnabled: true);

        Assert.NotNull(result);
        Assert.Equal(PanelViewMode.BriefTwoColumns, result.LeftViewMode);
        Assert.Equal(PanelViewMode.Full, result.RightViewMode);
        Assert.Equal("FarClassic", result.PaletteName);
        Assert.False(result.FileHighlightingEnabled);
        Assert.True(result.EditorSyntaxHighlightingEnabled);
    }

    [Fact]
    public void Show_EscapeReturnsNullAndRestoresTheme()
    {
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            var driver = Driver(
                Key(ConsoleKey.DownArrow),
                Key(ConsoleKey.DownArrow),
                Key(ConsoleKey.Enter),
                Key(ConsoleKey.Escape));

            SettingsDialogResult? result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
                PanelViewMode.Full,
                PanelViewMode.Full,
                "Default",
                fileHighlightingEnabled: true,
                editorSyntaxHighlightingEnabled: true);

            Assert.Null(result);
            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }

    [Fact]
    public void Show_HidesCursorOnInitialAndChangedFocusFrames()
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Escape));
        var cursorStates = new List<bool>();
        driver.BeforeReadInput = d => cursorStates.Add(d.CursorVisible);

        _ = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            PanelViewMode.Full,
            PanelViewMode.Full,
            "Default",
            fileHighlightingEnabled: true,
            editorSyntaxHighlightingEnabled: true);

        Assert.Equal([false], cursorStates);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void Show_ResizePreservesChangedValueAndLogicalFocusTarget()
    {
        var driver = Driver(
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow),
            new ConsoleResizeInputEvent(),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.F10));
        ResizeBeforeRead(driver, readNumber: 3, width: 100, height: 30);

        SettingsDialogResult? result = new SettingsDialog(ModalTestHost.Create(driver)).Show(
            PanelViewMode.Full,
            PanelViewMode.Full,
            "Default",
            fileHighlightingEnabled: true,
            editorSyntaxHighlightingEnabled: true);

        Assert.NotNull(result);
        Assert.Equal(PanelViewMode.BriefTwoColumns, result.LeftViewMode);
        Assert.Equal(PanelViewMode.BriefTwoColumns, result.RightViewMode);
        Assert.Equal("Default", result.PaletteName);
        Assert.True(result.FileHighlightingEnabled);
        Assert.True(result.EditorSyntaxHighlightingEnabled);
    }

    [Fact]
    public void TemporaryThemeScope_RestoresThemeAfterException()
    {
        UiTheme.ResetForTests();
        UiTheme.Initialize(PaletteRegistry.Default);
        try
        {
            void ThrowDuringTemporaryScope()
            {
                using (UiTheme.UseTemporary(PaletteRegistry.FarClassic))
                {
                    Assert.Same(PaletteRegistry.FarClassic, UiTheme.Current);
                    throw new InvalidOperationException("render failed");
                }
            }

            Assert.Throws<InvalidOperationException>((Action)ThrowDuringTemporaryScope);

            Assert.Same(PaletteRegistry.Default, UiTheme.Current);
        }
        finally
        {
            UiTheme.ResetForTests();
        }
    }

    private static FakeConsoleDriver Driver(params ConsoleInputEvent[] inputs)
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        foreach (ConsoleInputEvent input in inputs)
            driver.EnqueueInput(input);
        return driver;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static void ResizeBeforeRead(FakeConsoleDriver driver, int readNumber, int width, int height)
    {
        int reads = 0;
        driver.BeforeReadInput = OnBeforeRead;

        void OnBeforeRead(FakeConsoleDriver current)
        {
            reads++;
            if (reads == readNumber)
                current.SetSize(width, height);
            else
                current.BeforeReadInput = OnBeforeRead;
        }
    }
}
