using CSharpFar.App.Dialogs;
using CSharpFar.Core.Comparison;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CompareOptionsDialogTests
{
    [Fact]
    public void BuildOptions_AcceptsCustomDepthHistoryOnlyForCustomMode()
    {
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("CompareOptionsDialogTests.Depth");
        var fields = new FormFieldFactory(provider);
        TextField customDepth = fields.Text("custom-depth", "7", historyId);
        TextField include = fields.Text("include", "*");
        TextField exclude = fields.Text("exclude", "");
        string? error = null;

        ComparisonOptions? standardDepth = CompareOptionsDialog.BuildOptions(
            CompareMode.FolderStructure,
            recursive: true,
            selectedOnly: false,
            depth: "2",
            customDepth,
            include,
            exclude,
            CompareMethod.Fast,
            TimestampTolerance.Exact,
            NameComparisonMode.SystemDefault,
            FileSetMatchMode.FileName,
            ref error);

        Assert.NotNull(standardDepth);
        Assert.Null(error);
        Assert.Empty(provider.Get(historyId).Items);

        ComparisonOptions? custom = CompareOptionsDialog.BuildOptions(
            CompareMode.FolderStructure,
            recursive: true,
            selectedOnly: false,
            depth: "Custom",
            customDepth,
            include,
            exclude,
            CompareMethod.Fast,
            TimestampTolerance.Exact,
            NameComparisonMode.SystemDefault,
            FileSetMatchMode.FileName,
            ref error);

        Assert.NotNull(custom);
        Assert.Null(error);
        Assert.Equal(["7"], provider.Get(historyId).Items);
    }
}
