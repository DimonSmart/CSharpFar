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
        var panels = new CSharpFarPanelSettings(
            PanelViewMode.BriefTwoColumns,
            PanelViewMode.Full,
            ShowHiddenAndSystemFiles: false,
            SelectFolders: true,
            RightClickSelectsFiles: false,
            SortFoldersByExtension: true,
            RememberLastDirectories: true,
            FileHighlightingEnabled: false,
            ShowStatusLine: true,
            ShowFilesTotalInformation: false,
            ShowFreeSize: true,
            ShowSortModeLetter: false,
            ShowParentDirectoryInRootFolders: true);

        CSharpFarSettingsDialogResult? result = new CSharpFarSettingsDialog(
            new DialogService(ModalTestHost.Create(driver), new FormFieldFactory(TextFieldHistoryTestProvider.Create()))).Show(
                panels,
                "FarClassic",
                editorSyntaxHighlightingEnabled: true);

        Assert.NotNull(result);
        Assert.Equal(panels, result.Panels);
        Assert.Equal("FarClassic", result.PaletteName);
        Assert.True(result.EditorSyntaxHighlightingEnabled);
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
                DefaultPanels(),
                "Default",
                editorSyntaxHighlightingEnabled: true);

        Assert.Null(result);
        Assert.Same(PaletteRegistry.Default, UiTheme.Current);
    }

    private static CSharpFarPanelSettings DefaultPanels() =>
        new(
            PanelViewMode.Full,
            PanelViewMode.Full,
            ShowHiddenAndSystemFiles: true,
            SelectFolders: true,
            RightClickSelectsFiles: true,
            SortFoldersByExtension: true,
            RememberLastDirectories: false,
            FileHighlightingEnabled: true,
            ShowStatusLine: true,
            ShowFilesTotalInformation: true,
            ShowFreeSize: false,
            ShowSortModeLetter: true,
            ShowParentDirectoryInRootFolders: false);

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
