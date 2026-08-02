using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CompareOptionsDialog
{
    private const int DialogWidth = 86;
    private const int DialogHeight = 26;

    private readonly FormFieldFactory _fields;
    private readonly ModalFormHost _formDialogs;

    public CompareOptionsDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public ComparisonOptions? Show(
        CompareMode mode,
        AppSettings.CompareSettings settings,
        FilePanelState leftPanel,
        FilePanelState rightPanel)
    {
        return RunLoop(mode, settings, leftPanel, rightPanel);
    }

    private ComparisonOptions? RunLoop(
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
        var buttons = new ButtonRow(
            [
                DialogButton.Default("compare", "Compare", 'C'),
                DialogButton.Cancel(hotKey: 'A'),
            ]);
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(BuildRows(
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
                FormFooter.ErrorAndButtons(() => error, buttons));

        return _formDialogs.Run(
            form,
            new ModalFormOptions(mode == CompareMode.FileSet ? "Compare file sets" : "Compare folders", DialogWidth, DialogHeight, 52, 12),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (routed, result) =>
            {
                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<ComparisonOptions?>.Complete(null);

                if (FormDialogInput.ShouldSubmit(routed, result, form))
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
                        return ModalDialogLoopResult<ComparisonOptions?>.Complete(options);
                }

                return ModalDialogLoopResult<ComparisonOptions?>.ContinueNoChange;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildRows(
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
        List<IFormRow> rows =
        [
            new LabelRow($"Left : {leftPanel.CurrentDirectory}", FarDialogStyles.Fill),
            new LabelRow($"Right: {rightPanel.CurrentDirectory}", FarDialogStyles.Fill),
            ContextSelection(leftPanel, rightPanel),
            new SeparatorRow(FarDialogStyles.Border),
            new LabelRow("Scan", FarDialogStyles.Fill),
            recursive,
            selectedOnly,
            depth,
        ];

        if (depth.Value == "Custom")
        {
            rows.Add(new LabelRow("Custom depth:", FarDialogStyles.Fill));
            rows.Add(FormControls.Text(customDepth));
        }

        rows.Add(new SeparatorRow(FarDialogStyles.Border));
        rows.Add(new LabelRow("Filters", FarDialogStyles.Fill));
        rows.Add(new LabelRow("Include masks (semicolon-separated):", FarDialogStyles.Fill));
        rows.Add(FormControls.Text(include));
        rows.Add(new LabelRow("Exclude masks (semicolon-separated):", FarDialogStyles.Fill));
        rows.Add(FormControls.Text(exclude));
        rows.Add(new SeparatorRow(FarDialogStyles.Border));
        rows.Add(new LabelRow("Comparison", FarDialogStyles.Fill));
        rows.Add(method);
        if (method.Value == CompareMethod.Fast)
            rows.Add(tolerance);
        rows.Add(nameComparison);
        if (mode == CompareMode.FileSet)
            rows.Add(fileSetMatch);
        return rows;
    }

    private static IFormRow ContextSelection(FilePanelState leftPanel, FilePanelState rightPanel)
    {
        int leftCount = leftPanel.SelectedPaths.Count;
        int rightCount = rightPanel.SelectedPaths.Count;
        return leftCount + rightCount == 0
            ? new LabelRow("Scope: current folders", FarDialogStyles.Fill)
            : new LabelRow($"Selected: left {leftCount}, right {rightCount}", FarDialogStyles.Fill);
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
