using CSharpFar.App.Editor;
using CSharpFar.App.Settings;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class MacOsTerminalSettingsUiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplicationPageDoesNotExposeObsoleteProvider(bool macOs)
    {
        var driver = new FakeConsoleDriver(100, 28);
        string? frame = null;
        driver.BeforeReadInput = current => frame ??= string.Join("\\n",
            Enumerable.Range(0, current.GetSize().Height).Select(current.GetRow));
        driver.EnqueueInput(Key(ConsoleKey.Escape));

        Assert.Null(Show(driver, "kitty", macOs));
        Assert.NotNull(frame);
        Assert.DoesNotContain("Preferred terminal", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("Terminal.app", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingOtherSettingsPreservesLegacyProviderValue()
    {
        var driver = new FakeConsoleDriver(100, 28);
        driver.EnqueueInput(Key(ConsoleKey.F10));

        var result = Show(driver, " KITTY ", macOs: true);

        Assert.NotNull(result);
        Assert.Equal("kitty", result.PreferredMacOsTerminal);
    }

    [Fact]
    public void CancelDoesNotReturnAnyChanges()
    {
        var driver = new FakeConsoleDriver(100, 28);
        driver.EnqueueInput(Key(ConsoleKey.Escape));
        Assert.Null(Show(driver, "terminal", macOs: true));
    }

    private static CSharpFarSettingsDialogResult? Show(
        FakeConsoleDriver driver, string preferred, bool macOs)
    {
        var dialogs = new DialogService(
            ModalTestHost.Create(driver),
            new FormFieldFactory(TextFieldHistoryTestProvider.Create()));
        return new CSharpFarSettingsDialog(dialogs).Show(
            applicationConfirmExit: true,
            panels: new CSharpFarPanelSettings(
                PanelViewMode.Full, PanelViewMode.Full,
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
                ShowParentDirectoryInRootFolders: false),
            paletteName: "Default",
            editorSyntaxHighlightingEnabled: true,
            editorSyntaxTheme: "Dark+",
            syntaxCatalog: new TextMateEditorSyntaxCatalog(new AppSettings.EditorSettings()),
            preferredMacOsTerminal: preferred,
            isMacOs: macOs);
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
}
