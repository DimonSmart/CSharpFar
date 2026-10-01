using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CompareOptionsDialog
{
    private const int DialogWidth = 86;
    private const int DialogHeight = 30;

    private readonly FormFieldFactory _fields;
    private readonly DialogService _dialogs;

    public CompareOptionsDialog(DialogService dialogs, FormFieldFactory fields)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public ComparisonOptions? Show(
        CompareMode mode,
        AppSettings.CompareSettings settings,
        FilePanelState leftPanel,
        FilePanelState rightPanel)
    {
        TextField include = _fields.Text(new TextFieldOptions(
            string.IsNullOrWhiteSpace(settings.IncludeMasks) ? "*" : settings.IncludeMasks,
            AppTextHistoryIds.CompareInclude,
            SubmitOnEnter: true));
        TextField exclude = _fields.Text(new TextFieldOptions(
            settings.ExcludeMasks ?? "",
            AppTextHistoryIds.CompareExclude,
            SubmitOnEnter: true));
        TextField customDepth = _fields.Text(new TextFieldOptions(
            Math.Max(0, settings.CustomDepth).ToString(System.Globalization.CultureInfo.InvariantCulture),
            AppTextHistoryIds.CompareDepth,
            Width: 8,
            SubmitOnEnter: true));

        var recursive = FormControls.CheckBox("Include subfolders", settings.IncludeSubfolders);
        var selectedOnly = FormControls.CheckBox("Selected items only", settings.SelectedItemsOnly);
        var depth = FormControls.Choice(
            "Depth:", ["All", "0", "1", "2", "Custom"], static value => value, settings.Depth, "All");
        var method = FormControls.Dropdown(
            "Method:", [CompareMethod.Fast, CompareMethod.Content, CompareMethod.Text], MethodLabel,
            ParseEnum(settings.Method, CompareMethod.Fast));
        var textLineEndings = FormControls.Choice(
            "Line endings:", [TextLineEndingComparison.Exact, TextLineEndingComparison.Normalize], TextLineEndingsLabel,
            ParseEnum(settings.TextLineEndings, TextLineEndingComparison.Normalize));
        var textWhitespace = FormControls.Choice(
            "Whitespace:", [TextWhitespaceMode.Exact, TextWhitespaceMode.Normalize, TextWhitespaceMode.IgnoreAll], TextWhitespaceLabel,
            ParseEnum(settings.TextWhitespace, TextWhitespaceMode.Normalize));
        var textBom = FormControls.Choice(
            "BOM:", [TextBomComparison.Exact, TextBomComparison.Ignore], TextBomLabel,
            ParseEnum(settings.TextBom, TextBomComparison.Ignore));
        var tolerance = FormControls.Choice(
            "Timestamp:", [TimestampTolerance.Exact, TimestampTolerance.TwoSeconds, TimestampTolerance.OneHour], ToleranceLabel,
            ParseEnum(settings.TimestampTolerance, TimestampTolerance.Exact));
        var nameComparison = FormControls.Dropdown(
            "Name:", [NameComparisonMode.CaseSensitive, NameComparisonMode.CaseInsensitive], NameComparisonLabel,
            ParseEnum(settings.NameComparison, NameComparisonDefaults.Current));
        var fileSetMatch = FormControls.Dropdown(
            "Match by:", [FileSetMatchMode.FileName, FileSetMatchMode.FileNameAndSize, FileSetMatchMode.FileNameAndContentHash], FileSetMatchLabel,
            ParseEnum(settings.FileSetMatchMode, FileSetMatchMode.FileName));
        var buttons = FormControls.Buttons(
            [
                DialogButton.Default("compare", "Compare", 'C'),
                DialogButton.Cancel(hotKey: 'A'),
            ]);
        return _dialogs.Form(
            new FormDialogOptions(
                mode == CompareMode.FileSet ? "Compare file sets" : "Compare folders",
                DialogWidth,
                DialogHeight,
                52,
                12),
            rows: () => BuildRows(
                mode,
                leftPanel,
                rightPanel,
                recursive,
                selectedOnly,
                depth,
                customDepth,
                include,
                exclude,
                method,
                textLineEndings,
                textWhitespace,
                textBom,
                tolerance,
                nameComparison,
                fileSetMatch),
            footer: () => [buttons],
            submit: () =>
            {
                return BuildOptions(
                    mode, recursive.Value, selectedOnly.Value, depth.Value, customDepth, include, exclude,
                    method.Value, tolerance.Value, nameComparison.Value, fileSetMatch.Value,
                    textLineEndings.Value, textWhitespace.Value, textBom.Value);
            });
    }

    private static IReadOnlyList<FormRow> BuildRows(
        CompareMode mode,
        FilePanelState leftPanel,
        FilePanelState rightPanel,
        CheckBoxRow recursive,
        CheckBoxRow selectedOnly,
        ChoiceFormRow<string> depth,
        TextField customDepth,
        TextField include,
        TextField exclude,
        DropdownSelectFormRow<CompareMethod> method,
        ChoiceFormRow<TextLineEndingComparison> textLineEndings,
        ChoiceFormRow<TextWhitespaceMode> textWhitespace,
        ChoiceFormRow<TextBomComparison> textBom,
        ChoiceFormRow<TimestampTolerance> tolerance,
        DropdownSelectFormRow<NameComparisonMode> nameComparison,
        DropdownSelectFormRow<FileSetMatchMode> fileSetMatch)
    {
        List<FormRow> rows =
        [
            FormControls.Label($"Left : {leftPanel.CurrentDirectory}"),
            FormControls.Label($"Right: {rightPanel.CurrentDirectory}"),
            ContextSelection(leftPanel, rightPanel),
            FormControls.Separator(),
            FormControls.Label("Scan"),
            recursive,
            selectedOnly,
            depth,
        ];

        if (depth.Value == "Custom")
        {
            rows.Add(FormControls.Label("Custom depth:"));
            rows.Add(FormControls.Text(customDepth));
        }

        rows.Add(FormControls.Separator());
        rows.Add(FormControls.Label("Filters"));
        rows.Add(FormControls.Label("Include masks (semicolon-separated):"));
        rows.Add(FormControls.Text(include));
        rows.Add(FormControls.Label("Exclude masks (semicolon-separated):"));
        rows.Add(FormControls.Text(exclude));
        rows.Add(FormControls.Separator());
        rows.Add(FormControls.Label("Comparison"));
        rows.Add(nameComparison);
        if (mode == CompareMode.FileSet)
            rows.Add(fileSetMatch);
        rows.Add(FormControls.Separator());
        rows.Add(method);
        if (method.Value == CompareMethod.Fast)
            rows.Add(tolerance);
        else if (method.Value == CompareMethod.Text)
        {
            rows.Add(textLineEndings);
            rows.Add(textWhitespace);
            rows.Add(textBom);
        }
        return rows;
    }

    private static FormRow ContextSelection(FilePanelState leftPanel, FilePanelState rightPanel)
    {
        int leftCount = leftPanel.SelectedPaths.Count;
        int rightCount = rightPanel.SelectedPaths.Count;
        return leftCount + rightCount == 0
            ? FormControls.Label("Scope: current folders")
            : FormControls.Label($"Selected: left {leftCount}, right {rightCount}");
    }

    internal static FormSubmitResult<ComparisonOptions?> BuildOptions(
        CompareMode mode,
        bool recursive,
        bool selectedOnly,
        string depth,
        TextField customDepth,
        TextField include,
        TextField exclude,
        CompareMethod method,
        TimestampTolerance tolerance,
        NameComparisonMode nameComparison,
        FileSetMatchMode fileSetMatch,
        TextLineEndingComparison textLineEndings = TextLineEndingComparison.Normalize,
        TextWhitespaceMode textWhitespace = TextWhitespaceMode.Normalize,
        TextBomComparison textBom = TextBomComparison.Ignore)
    {
        string? error = null;
        int? maxDepth = depth switch
        {
            "All" => null,
            "0" => 0,
            "1" => 1,
            "2" => 2,
            _ => TryParseCustomDepth(customDepth.Text, out error),
        };

        if (error is not null)
        {
            IFormFocusTarget focusTarget = depth == "Custom" ? customDepth : include;
            return FormSubmit.Invalid<ComparisonOptions?>(error, focusTarget);
        }

        string includeMasks = string.IsNullOrWhiteSpace(include.Text) ? "*" : include.TrimmedText;
        string excludeMasks = exclude.TrimmedText;
        return FormSubmit.Success<ComparisonOptions?>(new ComparisonOptions
        {
            Mode = mode,
            IncludeSubfolders = recursive,
            SelectedItemsOnly = selectedOnly,
            MaxDepth = maxDepth,
            IncludeMasks = includeMasks,
            ExcludeMasks = excludeMasks,
            Method = method,
            TextLineEndings = textLineEndings,
            TextWhitespace = textWhitespace,
            TextBom = textBom,
            TimestampTolerance = tolerance,
            NameComparison = nameComparison,
            FileSetMatchMode = fileSetMatch,
        });
    }

    private static int? TryParseCustomDepth(string text, out string? error)
    {
        if (!int.TryParse(text.Trim(), out int value) || value < 0)
        {
            error = "Custom depth must be zero or a positive number.";
            return null;
        }

        error = null;
        return value;
    }

    private static string MethodLabel(CompareMethod method) =>
        method switch
        {
            CompareMethod.Fast => "Fast (size and modified time)",
            CompareMethod.Content => "Content (byte-by-byte)",
            CompareMethod.Text => "Text (normalized text)",
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };

    private static string TextLineEndingsLabel(TextLineEndingComparison mode) =>
        mode == TextLineEndingComparison.Normalize ? "Normalize" : "Exact";

    private static string TextWhitespaceLabel(TextWhitespaceMode mode) =>
        mode switch
        {
            TextWhitespaceMode.Exact => "Exact",
            TextWhitespaceMode.Normalize => "Normalize",
            TextWhitespaceMode.IgnoreAll => "Ignore all",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    private static string TextBomLabel(TextBomComparison mode) =>
        mode == TextBomComparison.Ignore ? "Ignore" : "Exact presence";

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum =>
        Enum.TryParse(value, out T parsed) && Enum.IsDefined(parsed) ? parsed : fallback;

    private static string ToleranceLabel(TimestampTolerance tolerance) =>
        tolerance switch { TimestampTolerance.TwoSeconds => "2 seconds", TimestampTolerance.OneHour => "1 hour", _ => "Exact" };

    private static string NameComparisonLabel(NameComparisonMode mode) =>
        mode switch
        {
            NameComparisonMode.CaseSensitive => "Case-sensitive",
            NameComparisonMode.CaseInsensitive => "Case-insensitive",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    private static string FileSetMatchLabel(FileSetMatchMode mode) =>
        mode switch { FileSetMatchMode.FileNameAndSize => "File name + size", FileSetMatchMode.FileNameAndContentHash => "File name + content hash", _ => "File name" };
}
