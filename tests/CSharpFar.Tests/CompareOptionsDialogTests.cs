using System.Reflection;
using CSharpFar.App.Dialogs;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class CompareOptionsDialogTests
{
    [Fact]
    public void Show_F10ReturnsDefaultFolderOptions()
    {
        var driver = Driver(Key(ConsoleKey.F10));

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, new AppSettings.CompareSettings());

        Assert.NotNull(result);
        Assert.Equal(CompareMode.FolderStructure, result.Mode);
        Assert.True(result.IncludeSubfolders);
        Assert.False(result.SelectedItemsOnly);
        Assert.Null(result.MaxDepth);
        Assert.Equal("*", result.IncludeMasks);
        Assert.Equal(string.Empty, result.ExcludeMasks);
        Assert.Equal(CompareMethod.Fast, result.Method);
        Assert.Equal(TimestampTolerance.Exact, result.TimestampTolerance);
        Assert.Equal(NameComparisonMode.SystemDefault, result.NameComparison);
        Assert.Equal(FileSetMatchMode.FileName, result.FileSetMatchMode);
    }

    [Fact]
    public void Show_EscapeReturnsNull()
    {
        var driver = Driver(Key(ConsoleKey.Escape));

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, new AppSettings.CompareSettings());

        Assert.Null(result);
    }

    [Fact]
    public void Show_FileSetPreservesConfiguredSemanticOptions()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var settings = new AppSettings.CompareSettings
        {
            IncludeSubfolders = false,
            SelectedItemsOnly = true,
            Depth = "1",
            IncludeMasks = "*.cs;*.md",
            ExcludeMasks = "bin;obj",
            Method = nameof(CompareMethod.Content),
            TimestampTolerance = nameof(TimestampTolerance.TwoSeconds),
            NameComparison = nameof(NameComparisonMode.CaseSensitive),
            FileSetMatchMode = nameof(FileSetMatchMode.FileNameAndSize),
        };

        ComparisonOptions? result = Show(driver, CompareMode.FileSet, settings);

        Assert.NotNull(result);
        Assert.Equal(CompareMode.FileSet, result.Mode);
        Assert.False(result.IncludeSubfolders);
        Assert.True(result.SelectedItemsOnly);
        Assert.Equal(1, result.MaxDepth);
        Assert.Equal("*.cs;*.md", result.IncludeMasks);
        Assert.Equal("bin;obj", result.ExcludeMasks);
        Assert.Equal(CompareMethod.Content, result.Method);
        Assert.Equal(TimestampTolerance.TwoSeconds, result.TimestampTolerance);
        Assert.Equal(NameComparisonMode.CaseSensitive, result.NameComparison);
        Assert.Equal(FileSetMatchMode.FileNameAndSize, result.FileSetMatchMode);
    }

    [Fact]
    public void BuildOptions_CustomDepthAndMasksProduceExpectedResult()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField customDepth = fields.Text(new TextFieldOptions("4"));
        TextField include = fields.Text(new TextFieldOptions("   "));
        TextField exclude = fields.Text(new TextFieldOptions("  *.tmp  "));

        object submit = CompareOptionsDialog.BuildOptions(
            CompareMode.FileSet,
            recursive: false,
            selectedOnly: true,
            depth: "Custom",
            customDepth,
            include,
            exclude,
            CompareMethod.Content,
            TimestampTolerance.OneHour,
            NameComparisonMode.CaseInsensitive,
            FileSetMatchMode.FileNameAndContentHash);

        Assert.True(ReadInternal<bool>(submit, "IsSuccess"));
        ComparisonOptions result = Assert.IsType<ComparisonOptions>(ReadInternal<object?>(submit, "Result"));
        Assert.Equal(4, result.MaxDepth);
        Assert.Equal("*", result.IncludeMasks);
        Assert.Equal("*.tmp", result.ExcludeMasks);
        Assert.False(result.IncludeSubfolders);
        Assert.True(result.SelectedItemsOnly);
        Assert.Equal(CompareMethod.Content, result.Method);
        Assert.Equal(TimestampTolerance.OneHour, result.TimestampTolerance);
        Assert.Equal(NameComparisonMode.CaseInsensitive, result.NameComparison);
        Assert.Equal(FileSetMatchMode.FileNameAndContentHash, result.FileSetMatchMode);
    }

    [Fact]
    public void BuildOptions_InvalidCustomDepthReportsValidationAndFocusesField()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField customDepth = fields.Text(new TextFieldOptions("-1"));
        TextField include = fields.Text(new TextFieldOptions("*"));
        TextField exclude = fields.Text();

        object submit = CompareOptionsDialog.BuildOptions(
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

        Assert.False(ReadInternal<bool>(submit, "IsSuccess"));
        Assert.Equal("Custom depth must be zero or a positive number.", ReadInternal<string?>(submit, "ErrorMessage"));
        Assert.Same(customDepth, ReadInternal<object?>(submit, "FocusTarget"));
        Assert.Null(ReadInternal<object?>(submit, "Result"));
    }

    [Fact]
    public void BuildOptions_LeavesHistoryCommitToTheFormSubmitLifecycle()
    {
        ITextFieldHistoryProvider provider = TextFieldHistoryTestProvider.Create();
        var historyId = new TextHistoryId("CompareOptionsDialogTests.Depth");
        var fields = new FormFieldFactory(provider);
        TextField customDepth = fields.Text(new TextFieldOptions("7", historyId));
        TextField include = fields.Text(new TextFieldOptions("*"));
        TextField exclude = fields.Text();
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

    private static ComparisonOptions? Show(
        FakeConsoleDriver driver,
        CompareMode mode,
        AppSettings.CompareSettings settings)
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        var dialogs = new DialogService(ModalTestHost.Create(driver), fields);
        return new CompareOptionsDialog(dialogs, fields).Show(
            mode,
            settings,
            new FilePanelState(),
            new FilePanelState());
    }

    private static FakeConsoleDriver Driver(params ConsoleKeyInfo[] keys)
    {
        var driver = new FakeConsoleDriver(width: 100, height: 35);
        foreach (ConsoleKeyInfo key in keys)
            driver.EnqueueKey(key);
        return driver;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static T ReadInternal<T>(object value, string propertyName)
    {
        PropertyInfo property = value.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing {propertyName} on {value.GetType().Name}.");
        return (T)property.GetValue(value)!;
    }
}
