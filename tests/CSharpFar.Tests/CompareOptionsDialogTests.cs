using CSharpFar.App.Dialogs;
using CSharpFar.Core.Comparison;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CompareOptionsDialogTests
{
    [Fact]
    public void BuildOptions_LeavesHistoryCommitToTheFormSubmitLifecycle()
    {
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("CompareOptionsDialogTests.Depth");
        var fields = new FormFieldFactory(provider);
        TextField customDepth = fields.Text("custom-depth", "7", historyId);
        TextField include = fields.Text("include", "*");
        TextField exclude = fields.Text("exclude", "");
        _ = CompareOptionsDialog.BuildOptions(
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
            FileSetMatchMode.FileName);

        Assert.Empty(provider.Get(historyId).Items);

        _ = CompareOptionsDialog.BuildOptions(
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
            FileSetMatchMode.FileName);

        Assert.Empty(provider.Get(historyId).Items);
    }
}
