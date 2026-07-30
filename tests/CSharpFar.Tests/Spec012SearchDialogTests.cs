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
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Case sensitive", StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(row.X + 1, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueKey(Key(ConsoleKey.F10));
        };

        var result = new SearchDialog(ModalTestHost.Create(screen)).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.True(result.CaseSensitive);
    }

    [Fact]
    public void Show_DefaultSearchScopeIsRecursive()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SearchDialog(ModalTestHost.Create(screen)).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal(SearchScope.CurrentDirectoryRecursive, result.Scope);
    }

    [Fact]
    public void TryCreateRequest_CurrentDirectoryOnlyPreservesScope()
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
            maxDegreeOfParallelismText: "4",
            out string? error);

        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal(SearchScope.CurrentDirectoryOnly, request.Scope);
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

        _ = new SearchDialog(ModalTestHost.Create(screen)).Show(@"C:\Work");
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

        var result = new SearchDialog(ModalTestHost.Create(screen)).Show(@"C:\Work");

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

        var result = new SearchDialog(ModalTestHost.Create(screen)).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.*abc", result.FileMaskExpression);
    }

    [Fact]
    public void Show_EnterOnNeutralMaskHistorySubmitsTypedMask()
    {
        var registry = new SingleLineTextHistoryRegistry();
        registry.GetOrCreate("SearchDialog.Mask").Add("*.cs");
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(CharKey('*'));
        driver.EnqueueKey(CharKey('.'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        SearchRequest? result = new SearchDialog(ModalTestHost.Create(screen), registry).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.c", result.FileMaskExpression);
    }

    [Fact]
    public void Show_ExplicitMaskHistorySelectionDoesNotSubmitUntilConfirmedAgain()
    {
        var registry = new SingleLineTextHistoryRegistry();
        registry.GetOrCreate("SearchDialog.Mask").Add("*.cs");
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(CharKey('*'));
        driver.EnqueueKey(CharKey('.'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(CharKey('x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SearchRequest? result = new SearchDialog(ModalTestHost.Create(screen), registry).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.csx", result.FileMaskExpression);
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

    [Fact]
    public void Enter_SubmitsFromRowsMarkedForSubmission()
    {
        IReadOnlyList<IFormRow> rows = BuildSearchRows(
            new TextInputRowState(),
            new TextInputRowState(),
            new TextInputRowState());
        foreach (string rowId in new[] { "mask", "text", "parallelism" })
        {
            TextInputRow row = Assert.Single(rows.OfType<TextInputRow>(), row => row.Id == rowId);
            Assert.True(row.SubmitOnEnter);
        }
    }

    [Fact]
    public void BuildRows_UsesCheckBoxColumnsAndDropdownScope()
    {
        IReadOnlyList<IFormRow> rows = BuildSearchRows(
            new TextInputRowState(),
            new TextInputRowState(),
            new TextInputRowState());

        Assert.Contains(rows, row => row is CheckBoxColumnsRow { Id: "search-options" });
        Assert.Contains(rows, row => row is DropdownSelectFormRow<SearchScope> { Id: "scope" });
        Assert.Empty(rows.OfType<ChoiceFormRow<SearchScope>>());
    }

    [Fact]
    public void BuildRows_NotContainingEnabledTracksTextPresence()
    {
        var notContaining = new CheckBoxRow(new CheckBoxLine("Not containing"));

        _ = BuildSearchRows(
            new TextInputRowState(),
            new TextInputRowState(),
            new TextInputRowState(),
            hasText: false,
            notContaining: notContaining);

        Assert.False(notContaining.Enabled);

        _ = BuildSearchRows(
            new TextInputRowState(),
            new TextInputRowState(),
            new TextInputRowState(),
            hasText: true,
            notContaining: notContaining);

        Assert.True(notContaining.Enabled);
    }

    private static IReadOnlyList<IFormRow> BuildSearchRows(
        TextInputRowState maskRowState,
        TextInputRowState textRowState,
        TextInputRowState parallelismRowState,
        bool hasText = true,
        CheckBoxRow? notContaining = null)
    {
        var method = typeof(SearchDialog).GetMethod(
            "BuildBodyRows",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("SearchDialog.BuildBodyRows was not found.");
        var caseSensitive = new CheckBoxRow(new CheckBoxLine("Case sensitive"));
        var wholeWords = new CheckBoxRow(new CheckBoxLine("Whole words"));
        notContaining ??= new CheckBoxRow(new CheckBoxLine("Not containing"));
        var includeDirectories = new CheckBoxRow(new CheckBoxLine("Include folders in results"));
        var searchLinks = new CheckBoxRow(new CheckBoxLine("Search in symbolic links"));
        var options = new CheckBoxColumnsRow(
            [
                [caseSensitive, wholeWords, notContaining],
                [includeDirectories, searchLinks],
            ])
        {
            Id = "search-options",
        };
        var dropdown = new DropdownSelect<SearchScope>(
            [SearchScope.CurrentDirectoryRecursive, SearchScope.CurrentDirectoryOnly],
            static scope => scope.ToString());

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
                notContaining,
                options,
                new DropdownSelectFormRow<SearchScope>(string.Empty, dropdown) { Id = "scope" },
                hasText,
            ])!;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo CharKey(char ch) =>
        new(ch, (ConsoleKey)char.ToUpperInvariant(ch), shift: false, alt: false, control: false);
}
