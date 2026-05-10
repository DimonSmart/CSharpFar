using CSharpFar.App.Dialogs;
using CSharpFar.Core.Models;

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
}
