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

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public CompareOptionsDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
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
        var include = new CommandLineState();
        include.SetText(string.IsNullOrWhiteSpace(settings.IncludeMasks) ? "*" : settings.IncludeMasks);
        var exclude = new CommandLineState();
        exclude.SetText(settings.ExcludeMasks ?? "");
        var customDepth = new CommandLineState();
        customDepth.SetText(Math.Max(0, settings.CustomDepth).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var includeHistory = HistoryRegistry.GetOrCreate("Compare.Include");
        var excludeHistory = HistoryRegistry.GetOrCreate("Compare.Exclude");
        var depthHistory = HistoryRegistry.GetOrCreate("Compare.Depth");
        var includeRowState = new TextInputRowState();
        var excludeRowState = new TextInputRowState();
        var depthRowState = new TextInputRowState();

        var recursive = new CheckBoxRow(new CheckBoxLine("Include subfolders", settings.IncludeSubfolders));
        var selectedOnly = new CheckBoxRow(new CheckBoxLine("Selected items only", settings.SelectedItemsOnly));
        var depth = new ChoiceFormRow<string>(
            new ChoiceRow<string>(["All", "0", "1", "2", "Custom"], x => x, DepthIndex(settings.Depth)),
            "Depth:");
        var method = new ChoiceFormRow<CompareMethod>(
            new ChoiceRow<CompareMethod>([CompareMethod.Fast, CompareMethod.Content], MethodLabel, MethodIndex(settings.Method)),
            "Method:");
        var tolerance = new ChoiceFormRow<TimestampTolerance>(
            new ChoiceRow<TimestampTolerance>([TimestampTolerance.Exact, TimestampTolerance.TwoSeconds, TimestampTolerance.OneHour], ToleranceLabel, ToleranceIndex(settings.TimestampTolerance)),
            "Timestamp:");
        var nameComparison = new ChoiceFormRow<NameComparisonMode>(
            new ChoiceRow<NameComparisonMode>([NameComparisonMode.SystemDefault, NameComparisonMode.CaseSensitive, NameComparisonMode.CaseInsensitive], NameComparisonLabel, NameComparisonIndex(settings.NameComparison)),
            "Name comparison:");
        var fileSetMatch = new ChoiceFormRow<FileSetMatchMode>(
            new ChoiceRow<FileSetMatchMode>([FileSetMatchMode.FileName, FileSetMatchMode.FileNameAndSize, FileSetMatchMode.FileNameAndContentHash], FileSetMatchLabel, FileSetIndex(settings.FileSetMatchMode)),
            "Match by:");
        var buttons = new ButtonRow(
            [
                new DialogButton("compare", "Compare", 'C', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'A'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput);
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
                depthHistory,
                depthRowState,
                include,
                exclude,
                includeHistory,
                excludeHistory,
                includeRowState,
                excludeRowState,
                method,
                tolerance,
                nameComparison,
                fileSetMatch,
                buttons));

        return _modalDialogs.RunInteractive<ScrollableFormFrame, FormInputResult, ComparisonOptions?>(
            (context, focusScope) => RenderLayer(context, focusScope, mode == CompareMode.FileSet ? "Compare file sets" : "Compare folders", form, error),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (result.FormResult, result.UiResult);
            },
            (routed, result) =>
            {
            if (result.Kind == FormInputResultKind.Cancel)
                return ModalDialogLoopResult<ComparisonOptions?>.Complete(null);

            if (result.Kind == FormInputResultKind.Submit ||
                routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
            {
                var options = BuildOptions(
                    mode,
                    recursive.Value,
                    selectedOnly.Value,
                    depth.Value,
                    customDepth.Text,
                    include.Text,
                    exclude.Text,
                    method.Value,
                    tolerance.Value,
                    nameComparison.Value,
                    fileSetMatch.Value,
                    includeHistory,
                    excludeHistory,
                    depthHistory,
                    ref error);
                if (options is not null)
                    return ModalDialogLoopResult<ComparisonOptions?>.Complete(options);
            }

            return ModalDialogLoopResult<ComparisonOptions?>.Continue;
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
        CommandLineState customDepth,
        SingleLineTextHistoryState depthHistory,
        TextInputRowState depthRowState,
        CommandLineState include,
        CommandLineState exclude,
        SingleLineTextHistoryState includeHistory,
        SingleLineTextHistoryState excludeHistory,
        TextInputRowState includeRowState,
        TextInputRowState excludeRowState,
        ChoiceFormRow<CompareMethod> method,
        ChoiceFormRow<TimestampTolerance> tolerance,
        ChoiceFormRow<NameComparisonMode> nameComparison,
        ChoiceFormRow<FileSetMatchMode> fileSetMatch,
        ButtonRow buttons)
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
            rows.Add(new TextInputRow(customDepth, depthHistory, depthRowState, width: 8) { SubmitOnEnter = true });
        }

        rows.Add(new SeparatorRow(FarDialogStyles.Border));
        rows.Add(new LabelRow("Filters", FarDialogStyles.Fill));
        rows.Add(new LabelRow("Include masks (semicolon-separated):", FarDialogStyles.Fill));
        rows.Add(new TextInputRow(include, includeHistory, includeRowState) { SubmitOnEnter = true });
        rows.Add(new LabelRow("Exclude masks (semicolon-separated):", FarDialogStyles.Fill));
        rows.Add(new TextInputRow(exclude, excludeHistory, excludeRowState) { SubmitOnEnter = true });
        rows.Add(new SeparatorRow(FarDialogStyles.Border));
        rows.Add(new LabelRow("Comparison", FarDialogStyles.Fill));
        rows.Add(method);
        if (method.Value == CompareMethod.Fast)
            rows.Add(tolerance);
        rows.Add(nameComparison);
        if (mode == CompareMode.FileSet)
            rows.Add(fileSetMatch);
        rows.Add(new SeparatorRow(FarDialogStyles.Fill, drawLine: false));
        rows.Add(buttons);
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

    private static ComparisonOptions? BuildOptions(
        CompareMode mode,
        bool recursive,
        bool selectedOnly,
        string depth,
        string customDepthText,
        string include,
        string exclude,
        CompareMethod method,
        TimestampTolerance tolerance,
        NameComparisonMode nameComparison,
        FileSetMatchMode fileSetMatch,
        SingleLineTextHistoryState includeHistory,
        SingleLineTextHistoryState excludeHistory,
        SingleLineTextHistoryState depthHistory,
        ref string? error)
    {
        error = null;
        int? maxDepth = depth switch
        {
            "All" => null,
            "0" => 0,
            "1" => 1,
            "2" => 2,
            _ => TryParseCustomDepth(customDepthText, ref error),
        };

        if (error is not null)
            return null;

        string includeMasks = string.IsNullOrWhiteSpace(include) ? "*" : include.Trim();
        string excludeMasks = exclude.Trim();
        includeHistory.Add(includeMasks);
        if (excludeMasks.Length > 0)
            excludeHistory.Add(excludeMasks);
        if (maxDepth.HasValue)
            depthHistory.Add(maxDepth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        includeHistory.Close();
        excludeHistory.Close();
        depthHistory.Close();

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

    private ScrollableFormFrame RenderLayer(UiRenderContext context, UiFocusScope focusScope, string title, ScrollableFormDialog form, string? error)
    {
        Rect outerBounds = _modalRenderer.CenteredOuterBounds(context.Size, DialogWidth, DialogHeight, minWidth: 52, minHeight: 12);
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(context.Screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int errorY = bounds.Y + bounds.Height - 2;
            frame = form.Render(new FormRenderContext(
                context,
                new Rect(contentX, bounds.Y + 1, contentWidth, Math.Max(1, errorY - bounds.Y - 1)),
                FarDialogStyles.Border),
                focusScope);
            context.Screen.Write(contentX, errorY, (error ?? "").PadRight(contentWidth), FarDialogStyles.Error);
        });
        return frame ?? throw new InvalidOperationException("Compare options dialog did not render a form frame.");
    }

    private static int DepthIndex(string value) => value switch
    {
        "0" => 1,
        "1" => 2,
        "2" => 3,
        "Custom" => 4,
        _ => 0,
    };

    private static int MethodIndex(string value) =>
        Enum.TryParse<CompareMethod>(value, out var parsed) && parsed == CompareMethod.Content ? 1 : 0;

    private static int ToleranceIndex(string value) =>
        Enum.TryParse<TimestampTolerance>(value, out var parsed)
            ? parsed switch { TimestampTolerance.TwoSeconds => 1, TimestampTolerance.OneHour => 2, _ => 0 }
            : 0;

    private static int NameComparisonIndex(string value) =>
        Enum.TryParse<NameComparisonMode>(value, out var parsed)
            ? parsed switch { NameComparisonMode.CaseSensitive => 1, NameComparisonMode.CaseInsensitive => 2, _ => 0 }
            : 0;

    private static int FileSetIndex(string value) =>
        Enum.TryParse<FileSetMatchMode>(value, out var parsed)
            ? parsed switch { FileSetMatchMode.FileNameAndSize => 1, FileSetMatchMode.FileNameAndContentHash => 2, _ => 0 }
            : 0;

    private static string MethodLabel(CompareMethod method) =>
        method == CompareMethod.Content ? "Content (byte-by-byte)" : "Fast (size and modified time)";

    private static string ToleranceLabel(TimestampTolerance tolerance) =>
        tolerance switch { TimestampTolerance.TwoSeconds => "2 seconds", TimestampTolerance.OneHour => "1 hour", _ => "Exact" };

    private static string NameComparisonLabel(NameComparisonMode mode) =>
        mode switch { NameComparisonMode.CaseSensitive => "Case-sensitive", NameComparisonMode.CaseInsensitive => "Case-insensitive", _ => "System default" };

    private static string FileSetMatchLabel(FileSetMatchMode mode) =>
        mode switch { FileSetMatchMode.FileNameAndSize => "File name + size", FileSetMatchMode.FileNameAndContentHash => "File name + content hash", _ => "File name" };
}
