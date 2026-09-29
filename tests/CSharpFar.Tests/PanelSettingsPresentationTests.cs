using CSharpFar.App.Editor;
using CSharpFar.App.Settings;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class PanelSettingsPresentationTests
{
    private static readonly string[] ExpectedPanelTextOrder =
    [
        "View",
        "Left panel view: Full",
        "Right panel view: Full",
        "Show panel status line",
        "Show file count and total size",
        "Show free disk space",
        "Show current sort mode indicator",
        "Files and folders",
        "Show hidden and system files",
        "Enable file highlighting",
        "Sort folders by extension",
        "Show \"..\" entry in filesystem roots",
        "Interaction",
        "Allow folders to be selected",
        "Right-click selects/deselects items",
        "Restore last opened panel folders on startup",
    ];

    [Fact]
    public void Show_PanelsPageRendersRequiredGroupsAndLabelsOnceInOrder()
    {
        var driver = Driver(
            80,
            25,
            Key(ConsoleKey.DownArrow));
        string? firstFrame = null;
        void PrepareNextInput(FakeConsoleDriver current)
        {
            current.BeforeReadInput = PrepareNextInput;
            current.BeforeTryReadInput = PrepareNextInput;

            if (current.PendingInputCount > 0)
                return;

            string frame = ScreenText(current);
            if (frame.Contains("Left panel view", StringComparison.Ordinal))
            {
                firstFrame ??= frame;
                current.EnqueueInput(Key(ConsoleKey.F10));
                return;
            }

            if (current.LastDequeuedInput is KeyConsoleInputEvent { Key.Key: ConsoleKey.DownArrow })
            {
                current.EnqueueInput(Key(ConsoleKey.RightArrow));
                return;
            }

            current.EnqueueInput(Key(ConsoleKey.F10));
        }

        driver.BeforeReadInput = PrepareNextInput;
        driver.BeforeTryReadInput = PrepareNextInput;

        CSharpFarSettingsDialogResult? result = Show(driver, DefaultPanels());

        Assert.NotNull(result);
        Assert.NotNull(firstFrame);
        AssertInOrder(firstFrame, ExpectedPanelTextOrder);

        foreach (string text in ExpectedPanelTextOrder)
            Assert.Equal(1, CountOccurrences(firstFrame, text));

        string[] obsoleteLabels =
        [
            "Left panel: ",
            "Right panel: ",
            "Select folders",
            "Right click selects files",
            "Remember last panel folders",
            "File highlighting",
            "Show status line",
            "Show files total information",
            "Show free size",
            "Show sort mode letter",
            "Show \"..\" in root folders",
        ];

        foreach (string obsoleteLabel in obsoleteLabels)
            Assert.DoesNotContain(obsoleteLabel, firstFrame, StringComparison.Ordinal);
    }

    [Fact]
    public void Show_EndReachesLastPanelControlAtLimitedHeight()
    {
        var driver = Driver(
            width: 80,
            height: 10,
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.End),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.F10));

        CSharpFarSettingsDialogResult? result = Show(driver, DefaultPanels());

        Assert.NotNull(result);
        Assert.True(result.Panels.RememberLastDirectories);
    }

    [Fact]
    public void Show_UpCrossesGroupBoundaryWithoutFocusingPresentationRows()
    {
        var driver = Driver(
            80,
            25,
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.End),
            Key(ConsoleKey.UpArrow),
            Key(ConsoleKey.UpArrow),
            Key(ConsoleKey.UpArrow),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.F10));

        CSharpFarSettingsDialogResult? result = Show(driver, DefaultPanels());

        Assert.NotNull(result);
        Assert.True(result.Panels.ShowParentDirectoryInRootFolders);
        Assert.True(result.Panels.SelectFolders);
        Assert.True(result.Panels.RightClickSelectsFiles);
        Assert.False(result.Panels.RememberLastDirectories);
    }

    private static CSharpFarSettingsDialogResult? Show(
        FakeConsoleDriver driver,
        CSharpFarPanelSettings panels) =>
        new CSharpFarSettingsDialog(
            new DialogService(
                ModalTestHost.Create(driver),
                new FormFieldFactory(TextFieldHistoryTestProvider.Create())))
            .Show(true, panels, "Default", editorSyntaxHighlightingEnabled: true,
                editorSyntaxTheme: "Dark+",
                syntaxCatalog: new TextMateEditorSyntaxCatalog(new AppSettings.EditorSettings()));

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

    private static FakeConsoleDriver Driver(
        int width,
        int height,
        params ConsoleInputEvent[] inputs)
    {
        var driver = new FakeConsoleDriver(width, height);
        foreach (ConsoleInputEvent input in inputs)
            driver.EnqueueInput(input);
        return driver;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));

    private static string ScreenText(FakeConsoleDriver driver) =>
        string.Join(
            '\n',
            Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow));

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static void AssertInOrder(string text, IReadOnlyList<string> expected)
    {
        int previous = -1;
        foreach (string value in expected)
        {
            int current = text.IndexOf(value, previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{value}' after offset {previous}.");
            previous = current;
        }
    }
}
