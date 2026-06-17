using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class Spec012SearchDialogTests
{
    [Fact]
    public void TryCreateRequest_NormalizesEmptyMaskAndDefaultTextOptions()
    {
        var request = SearchDialog.TryCreateRequest(
            rootPath: @"C:\Work",
            fileMaskExpression: "   ",
            containingText: string.Empty,
            caseSensitive: true,
            wholeWords: true,
            notContaining: true,
            includeDirectoriesInResults: true,
            searchInSymbolicLinks: false,
            scope: SearchScope.CurrentDirectoryRecursive,
            maxDegreeOfParallelismText: "4",
            out string? error);

        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("*", request.FileMaskExpression);
        Assert.Null(request.ContainingText);
        Assert.False(request.NotContaining);
        Assert.True(request.IncludeDirectoriesInResults);
    }

    [Fact]
    public void TryCreateRequest_InvalidParallelismBlocksRequest()
    {
        var request = SearchDialog.TryCreateRequest(
            rootPath: @"C:\Work",
            fileMaskExpression: "*",
            containingText: string.Empty,
            caseSensitive: false,
            wholeWords: false,
            notContaining: false,
            includeDirectoriesInResults: false,
            searchInSymbolicLinks: false,
            scope: SearchScope.CurrentDirectoryOnly,
            maxDegreeOfParallelismText: "17",
            out string? error);

        Assert.Null(request);
        Assert.Equal("Parallelism must be a number from 1 to 16.", error);
    }

    [Fact]
    public void Show_MouseClickCheckboxTogglesSearchOption()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(16, 12, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SearchDialog(screen).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.True(result.CaseSensitive);
    }

    [Fact]
    public void Show_MouseClickSearchScopeChangesScope()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("In current folder", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("In current folder", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new SearchDialog(screen).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal(SearchScope.CurrentDirectoryOnly, result.Scope);
    }

    [Fact]
    public void Show_InitialMaskFieldShowsCursor()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            Assert.True(currentDriver.CursorVisible);
            Assert.True(currentDriver.CursorX > 0);
            Assert.True(currentDriver.CursorY > 0);
            currentDriver.EnqueueKey(Key(ConsoleKey.Escape));
        };

        _ = new SearchDialog(screen).Show(@"C:\Work");
    }

    [Fact]
    public void Show_TypingReplacesInitiallySelectedMask()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(CharKey('a'));
        driver.EnqueueKey(CharKey('b'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SearchDialog(screen).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("abc", result.FileMaskExpression);
    }

    [Fact]
    public void Show_RightArrowBeforeTypingKeepsInitialMask()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(CharKey('a'));
        driver.EnqueueKey(CharKey('b'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SearchDialog(screen).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.*abc", result.FileMaskExpression);
    }

    [Fact]
    public void BuildRows_ReusesSearchTextInputRowStates()
    {
        var maskRowState = new TextInputRowState();
        var textRowState = new TextInputRowState();
        var parallelismRowState = new TextInputRowState();
        var firstRows = BuildSearchRows(maskRowState, textRowState, parallelismRowState);
        var secondRows = BuildSearchRows(maskRowState, textRowState, parallelismRowState);

        var firstInputs = firstRows.OfType<TextInputRow>().ToArray();
        var secondInputs = secondRows.OfType<TextInputRow>().ToArray();

        Assert.Same(maskRowState, firstInputs[0].State);
        Assert.Same(textRowState, firstInputs[1].State);
        Assert.Same(parallelismRowState, firstInputs[2].State);
        Assert.Same(maskRowState, secondInputs[0].State);
        Assert.Same(textRowState, secondInputs[1].State);
        Assert.Same(parallelismRowState, secondInputs[2].State);
    }

    private static IReadOnlyList<IFormRow> BuildSearchRows(
        TextInputRowState maskRowState,
        TextInputRowState textRowState,
        TextInputRowState parallelismRowState)
    {
        var method = typeof(SearchDialog).GetMethod(
            "BuildRows",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("SearchDialog.BuildRows was not found.");

        return (IReadOnlyList<IFormRow>)method.Invoke(
            null,
            [
                new CommandLineState(),
                new CommandLineState(),
                new CommandLineState(),
                new SingleLineTextHistoryState(),
                new SingleLineTextHistoryState(),
                new SingleLineTextHistoryState(),
                maskRowState,
                textRowState,
                parallelismRowState,
                new CheckBoxRow(new CheckBoxLine("Case sensitive")),
                new CheckBoxRow(new CheckBoxLine("Whole words")),
                new CheckBoxRow(new CheckBoxLine("Not containing")),
                new CheckBoxRow(new CheckBoxLine("Search folders")),
                new CheckBoxRow(new CheckBoxLine("Search in symbolic links")),
                new ChoiceFormRow<SearchScope>(
                    new ChoiceRow<SearchScope>([SearchScope.CurrentDirectoryRecursive], static scope => scope.ToString()),
                    "Select search area:"),
                new ButtonRow(
                    [new DialogButton("find", "Find", 'F', IsDefault: true)],
                    FarDialogStyles.Fill,
                    FarDialogStyles.FocusedInput),
                true,
            ])!;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo CharKey(char ch) =>
        new(ch, (ConsoleKey)char.ToUpperInvariant(ch), shift: false, alt: false, control: false);
}
