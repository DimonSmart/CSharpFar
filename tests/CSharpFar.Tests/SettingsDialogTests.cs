using CSharpFar.App.Settings;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class SettingsDialogTests
{
    [Fact]
    public void Show_F10ReturnsExistingValues()
    {
        var driver = Driver(Key(ConsoleKey.F10));

        CSharpFarSettingsDialogResult? result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                PanelViewMode.BriefTwoColumns,
                PanelViewMode.Full,
                "FarClassic",
                fileHighlightingEnabled: false,
                editorSyntaxHighlightingEnabled: true,
                rememberLastDirectories: true);

        Assert.NotNull(result);
        Assert.Equal(PanelViewMode.BriefTwoColumns, result.LeftViewMode);
        Assert.Equal(PanelViewMode.Full, result.RightViewMode);
        Assert.Equal("FarClassic", result.PaletteName);
        Assert.False(result.FileHighlightingEnabled);
        Assert.True(result.EditorSyntaxHighlightingEnabled);
        Assert.True(result.RememberLastDirectories);
    }

    [Fact]
    public void Show_EscapeReturnsNullAndRestoresTheme()
    {
        using var theme = UiTheme.UseTemporary(PaletteRegistry.Default);
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Escape));

        CSharpFarSettingsDialogResult? result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                PanelViewMode.Full,
                PanelViewMode.Full,
                "Default",
                fileHighlightingEnabled: true,
                editorSyntaxHighlightingEnabled: true);

        Assert.Null(result);
        Assert.Same(PaletteRegistry.Default, UiTheme.Current);
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
}
