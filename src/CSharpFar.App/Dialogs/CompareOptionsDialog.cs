using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CompareOptionsDialog
{
    private const int DialogWidth = 86;
    private const int DialogHeight = 26;

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
        return ShowForm(mode, settings, leftPanel, rightPanel);
    }

    private ComparisonOptions? ShowForm(
        CompareMode mode,
        AppSettings.CompareSettings settings,
        FilePanelState leftPanel,
        FilePanelState rightPanel)
    {
        TextField include = _fields.Text("include", string.IsNullOrWhiteSpace(settings.IncludeMasks) ? "*" : settings.IncludeMasks, AppTextHistoryIds.CompareInclude, submitOnEnter: true);
        TextField exclude = _fields.Text("exclude", settings.ExcludeMasks ?? "", AppTextHistoryIds.CompareExclude, submitOnEnter: true);
        TextField customDepth = _fields.Text("custom-depth", Math.Max(0, settings.CustomDepth).ToString(System.Globalization.CultureInfo.InvariantCulture), AppTextHistoryIds.CompareDepth, width: 8, submitOnEnter: true);

        var recursive = FormControls.CheckBox("recursive", "Include subfolders", settings.IncludeSubfolders);
        var selectedOnly = FormControls.CheckBox("selected-only", "Selected items only", settings.SelectedItemsOnly);
        var depth = FormControls.Choice(
            "depth", "Depth:", ["All", "0", "1", "2", "Custom"], static value => value, settings.Depth, "All");
        var method = FormControls.Choice(
            "method", "Method:", [CompareMethod.Fast, CompareMethod.Content], MethodLabel,
            Enum.TryParse(settings.Method, out CompareMethod initialMethod) ? initialMethod : CompareMethod.Fast);
        var tolerance = FormControls.Choice(
            "tolerance", "Timestamp:", [TimestampTolerance.Exact, TimestampTolerance.TwoSeconds, TimestampTolerance.OneHour], ToleranceLabel,
            Enum.TryParse(settings.TimestampTolerance, out TimestampTolerance initialTolerance) ? initialTolerance : TimestampTolerance.Exact);
        var nameComparison = FormControls.Choice(
            "name-comparison", "Name comparison:", [NameComparisonMode.SystemDefault, NameComparisonMode.CaseSensitive, NameComparisonMode.CaseInsensitive], NameComparisonLabel,
            Enum.TryParse(settings.NameComparison, out NameComparisonMode initialNameComparison) ? initialNameComparison : NameComparisonMode.SystemDefault);
        var fileSetMatch = FormControls.Choice(
            "file-set-match", "Match by:", [FileSetMatchMode.FileName, FileSetMatchMode.FileNameAndSize, FileSetMatchMode.FileNameAndContentHash], FileSetMatchLabel,
            Enum.TryParse(settings.FileSetMatchMode, out FileSetMatchMode initialFileSetMatch) ? initialFileSetMatch : FileSetMatchMode.FileName);
        var buttons = FormControls.Buttons("actions",
            [
                DialogButton.Default("compare", "Compare", 'C'),
                DialogButton.Cancel(hotKey: 'A'),
            ]);
        string? error = null;

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
                tolerance,
                nameComparison,
                fileSetMatch),
            footer: () => [FormControls.Error(() => error), buttons],
            handle: result =>
            {
                if (result.IsCancelled)
                    return FormDialogOutcome<ComparisonOptions?>.Complete(null);

                if (result.IsSubmitted)
                {
                    var options = BuildOptions(
                        mode,
                        recursive.Value,
                        selectedOnly.Value,
                        depth.Value,
                        customDepth,
                        include,
                        exclude,
                        method.Value,
                        tolerance.Value,
                        nameComparison.Value,
                        fileSetMatch.Value,
                        ref error);
                    if (options is not null)
                        return FormDialogOutcome<ComparisonOptions?>.Complete(options);

                    return FormDialogOutcome<ComparisonOptions?>.ContinueWithFocus(
                        depth.Value == "Custom" ? customDepth.Id : depth.Id!);
                }

                return FormDialogOutcome<ComparisonOptions?>.Continue();
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
        ChoiceFormRow<CompareMethod> method,
        ChoiceFormRow<TimestampTolerance> tolerance,
        ChoiceFormRow<NameComparisonMode> nameComparison,
        ChoiceFormRow<FileSetMatchMode> fileSetMatch)
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
        rows.Add(method);
        if (method.Value == CompareMethod.Fast)
            rows.Add(tolerance);
        rows.Add(nameComparison);
        if (mode == CompareMode.FileSet)
            rows.Add(fileSetMatch);
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

    internal static ComparisonOptions? BuildOptions(
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
        ref string? error)
    {
        error = null;
        int? maxDepth = depth switch
        {
            "All" => null,
            "0" => 0,
            "1" => 1,
            "2" => 2,
            _ => TryParseCustomDepth(customDepth.Text, ref error),
        };

        if (error is not null)
            return null;

        string includeMasks = string.IsNullOrWhiteSpace(include.Text) ? "*" : include.TrimmedText;
        string excludeMasks = exclude.TrimmedText;
        include.AcceptHistory();
        if (excludeMasks.Length > 0)
            exclude.AcceptHistory();
        if (depth == "Custom")
            customDepth.AcceptHistory();

        return new ComparisonOptions
        {
            Mode = mode,
            IncludeSubfolders = recursive,
            SelectedItemsOnly = selectedOnly,
            MaxDepth = maxDepth,
            IncludeMasks = includeMasks,
            ExcludeMasks = excludeMasks,
            Method = method,
            TimestampTolerance = tolerance,
            NameComparison = nameComparison,
            FileSetMatchMode = fileSetMatch,
        };
    }

    private static int? TryParseCustomDepth(string text, ref string? error)
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
        method == CompareMethod.Content ? "Content (byte-by-byte)" : "Fast (size and modified time)";

    private static string ToleranceLabel(TimestampTolerance tolerance) =>
        tolerance switch { TimestampTolerance.TwoSeconds => "2 seconds", TimestampTolerance.OneHour => "1 hour", _ => "Exact" };

    private static string NameComparisonLabel(NameComparisonMode mode) =>
        mode switch { NameComparisonMode.CaseSensitive => "Case-sensitive", NameComparisonMode.CaseInsensitive => "Case-insensitive", _ => "System default" };

    private static string FileSetMatchLabel(FileSetMatchMode mode) =>
        mode switch { FileSetMatchMode.FileNameAndSize => "File name + size", FileSetMatchMode.FileNameAndContentHash => "File name + content hash", _ => "File name" };
}
