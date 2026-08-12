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

        var result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.True(result.CaseSensitive);
    }

    [Fact]
    public void Show_ConstrainedHeightFooterCancelButtonRemainsMouseAccessible()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 10);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(record =>
                record.Text.Contains("Cancel", StringComparison.Ordinal));
            int x = row.X + row.Text.IndexOf("Cancel", StringComparison.Ordinal);
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(x, row.Y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None));
        };

        var result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");

        Assert.Null(result);
    }

    [Fact]
    public void Show_DefaultSearchScopeIsRecursive()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");

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

        _ = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");
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

        var result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");

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

        var result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(TextFieldHistoryTestProvider.Create())), new FormFieldFactory(TextFieldHistoryTestProvider.Create())).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.*abc", result.FileMaskExpression);
    }

    [Fact]
    public void Show_EnterOnNeutralMaskHistorySubmitsTypedMask()
    {
        var registry = new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
        registry.Get(new TextHistoryId("SearchDialog.Mask")).Add("*.cs");
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(CharKey('*'));
        driver.EnqueueKey(CharKey('.'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        SearchRequest? result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(registry)), new FormFieldFactory(registry)).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.c", result.FileMaskExpression);
    }

    [Fact]
    public void Show_ExplicitMaskHistorySelectionDoesNotSubmitUntilConfirmedAgain()
    {
        var registry = new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore());
        registry.Get(new TextHistoryId("SearchDialog.Mask")).Add("*.cs");
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(CharKey('*'));
        driver.EnqueueKey(CharKey('.'));
        driver.EnqueueKey(CharKey('c'));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(CharKey('x'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        SearchRequest? result = new SearchDialog(new DialogService(ModalTestHost.Create(screen), new FormFieldFactory(registry)), new FormFieldFactory(registry)).Show(@"C:\Work");

        Assert.NotNull(result);
        Assert.Equal("*.csx", result.FileMaskExpression);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo CharKey(char ch) =>
        new(ch, (ConsoleKey)char.ToUpperInvariant(ch), shift: false, alt: false, control: false);
}
