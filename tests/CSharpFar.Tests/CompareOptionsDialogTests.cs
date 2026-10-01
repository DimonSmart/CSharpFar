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
        Assert.Equal(TextLineEndingComparison.Normalize, result.TextLineEndings);
        Assert.Equal(TextWhitespaceMode.Normalize, result.TextWhitespace);
        Assert.Equal(TextBomComparison.Ignore, result.TextBom);
        Assert.Equal(TimestampTolerance.Exact, result.TimestampTolerance);
        Assert.Equal(NameComparisonDefaults.Current, result.NameComparison);
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

    [Theory]
    [InlineData("SystemDefault")]
    [InlineData("")]
    [InlineData("invalid")]
    public void Show_LegacyOrInvalidNameComparisonFallsBackToPlatformDefault(string persistedValue)
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var settings = new AppSettings.CompareSettings
        {
            NameComparison = persistedValue,
        };

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, settings);

        Assert.NotNull(result);
        Assert.Equal(NameComparisonDefaults.Current, result.NameComparison);
    }

    [Theory]
    [InlineData(NameComparisonMode.CaseSensitive)]
    [InlineData(NameComparisonMode.CaseInsensitive)]
    public void Show_ExplicitNameComparisonIsPreserved(NameComparisonMode persistedValue)
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var settings = new AppSettings.CompareSettings
        {
            NameComparison = persistedValue.ToString(),
        };

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, settings);

        Assert.NotNull(result);
        Assert.Equal(persistedValue, result.NameComparison);
    }

    [Fact]
    public void Show_MethodDropdownCommitsContentAndRebuildsMethodSpecificRows()
    {
        var driver = Driver(
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.Tab),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Enter),
            Key(ConsoleKey.F10));

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, new AppSettings.CompareSettings());

        Assert.NotNull(result);
        Assert.Equal(CompareMethod.Content, result.Method);

        string screen = string.Join('\n', Enumerable.Range(0, 35).Select(driver.GetRow));
        Assert.Contains("Content (byte-by-byte)", screen);
        Assert.DoesNotContain("Timestamp:", screen);
        Assert.DoesNotContain("Line endings:", screen);
        Assert.DoesNotContain("Whitespace:", screen);
        Assert.DoesNotContain("BOM:", screen);
    }

    [Fact]
    public void Show_DefaultDropdownLayoutRendersOnlySelectedLongValues()
    {
        var driver = Driver(Key(ConsoleKey.F10));

        _ = Show(driver, CompareMode.FolderStructure, new AppSettings.CompareSettings());

        string screen = string.Join('\n', Enumerable.Range(0, 35).Select(driver.GetRow));
        Assert.Contains("Fast (size and modified time)", screen);
        Assert.DoesNotContain("Content (byte-by-byte)", screen);
        Assert.DoesNotContain("Text (normalized text)", screen);
        Assert.DoesNotContain("System default", screen);
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
            NameComparisonDefaults.Current,
            FileSetMatchMode.FileName);

        Assert.False(ReadInternal<bool>(submit, "IsSuccess"));
        Assert.Equal("Custom depth must be zero or a positive number.", ReadInternal<string?>(submit, "ErrorMessage"));
        Assert.Same(customDepth, ReadInternal<object?>(submit, "FocusTarget"));
        Assert.Null(ReadInternal<object?>(submit, "Result"));
    }

    [Fact]
    public void BuildOptions_FixedDepthIgnoresInvalidHiddenCustomDepth()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField customDepth = fields.Text(new TextFieldOptions("-1"));
        TextField include = fields.Text(new TextFieldOptions("*"));
        TextField exclude = fields.Text();

        object submit = CompareOptionsDialog.BuildOptions(
            CompareMode.FolderStructure,
            recursive: true,
            selectedOnly: false,
            depth: "2",
            customDepth,
            include,
            exclude,
            CompareMethod.Fast,
            TimestampTolerance.Exact,
            NameComparisonDefaults.Current,
            FileSetMatchMode.FileName);

        Assert.True(ReadInternal<bool>(submit, "IsSuccess"));
        ComparisonOptions result = Assert.IsType<ComparisonOptions>(ReadInternal<object?>(submit, "Result"));
        Assert.Equal(2, result.MaxDepth);
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
            NameComparisonDefaults.Current,
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
            NameComparisonDefaults.Current,
            FileSetMatchMode.FileName);

        Assert.Empty(provider.Get(historyId).Items);
    }

    [Fact]
    public void Show_TextRestoresPersistedTextOptions()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var settings = new AppSettings.CompareSettings
        {
            Method = nameof(CompareMethod.Text),
            TextLineEndings = nameof(TextLineEndingComparison.Exact),
            TextWhitespace = nameof(TextWhitespaceMode.IgnoreAll),
            TextBom = nameof(TextBomComparison.Exact),
        };

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, settings);

        Assert.NotNull(result);
        Assert.Equal(CompareMethod.Text, result.Method);
        Assert.Equal(TextLineEndingComparison.Exact, result.TextLineEndings);
        Assert.Equal(TextWhitespaceMode.IgnoreAll, result.TextWhitespace);
        Assert.Equal(TextBomComparison.Exact, result.TextBom);
    }

    [Fact]
    public void Show_InvalidPersistedTextOptionsFallBackToDefaults()
    {
        var driver = Driver(Key(ConsoleKey.F10));
        var settings = new AppSettings.CompareSettings
        {
            Method = "999",
            TextLineEndings = "invalid",
            TextWhitespace = "999",
            TextBom = "invalid",
        };

        ComparisonOptions? result = Show(driver, CompareMode.FolderStructure, settings);

        Assert.NotNull(result);
        Assert.Equal(CompareMethod.Fast, result.Method);
        Assert.Equal(TextLineEndingComparison.Normalize, result.TextLineEndings);
        Assert.Equal(TextWhitespaceMode.Normalize, result.TextWhitespace);
        Assert.Equal(TextBomComparison.Ignore, result.TextBom);
    }

    [Fact]
    public void BuildOptions_TransfersTextOptions()
    {
        var fields = new FormFieldFactory(TextFieldHistoryTestProvider.Create());
        TextField customDepth = fields.Text(new TextFieldOptions("3"));
        TextField include = fields.Text(new TextFieldOptions("*"));
        TextField exclude = fields.Text();

        object submit = CompareOptionsDialog.BuildOptions(
            CompareMode.FolderStructure, true, false, "All",
            customDepth, include, exclude,
            CompareMethod.Text, TimestampTolerance.Exact,
            NameComparisonDefaults.Current, FileSetMatchMode.FileName,
            TextLineEndingComparison.Exact,
            TextWhitespaceMode.IgnoreAll,
            TextBomComparison.Exact);

        ComparisonOptions result = Assert.IsType<ComparisonOptions>(ReadInternal<object?>(submit, "Result"));
        Assert.Equal(TextLineEndingComparison.Exact, result.TextLineEndings);
        Assert.Equal(TextWhitespaceMode.IgnoreAll, result.TextWhitespace);
        Assert.Equal(TextBomComparison.Exact, result.TextBom);
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
