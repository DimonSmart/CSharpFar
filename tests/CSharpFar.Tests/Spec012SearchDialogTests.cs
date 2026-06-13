using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

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

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
