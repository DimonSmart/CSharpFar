using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record FileOperationDialogResult(
    string Destination,
    FileOperationOptions Options);

internal sealed class FileOperationDialog
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 26;

    private static readonly ConflictDecisionMode[] CopyConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
        ConflictDecisionMode.OnlyNewer,
    ];

    private static readonly ConflictDecisionMode[] MoveConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
    ];

    private static readonly CopyMode[] LocalCopyModes =
    [
        CopyMode.Normal,
        CopyMode.Reliable,
        CopyMode.FastSalvage,
    ];

    private static readonly FileSecurityMode[] SecurityModes =
    [
        FileSecurityMode.Default,
        FileSecurityMode.CopyAccessControl,
        FileSecurityMode.Inherit,
    ];

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FileOperationDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public FileOperationDialogResult? ShowCopy(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? $"Copy {Path.GetFileName(sources[0])} to:"
            : $"Copy {sources.Count} items to:";
        return Show("Copy", prompt, "Copy", initialDestination, initialOptions, CopyConflictModes, LocalCopyModes, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowMove(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? "Move / Rename to:"
            : $"Move {sources.Count} items to:";
        return Show("Move", prompt, "Move", initialDestination, initialOptions, MoveConflictModes, copyModes: null, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowRename(
        string source,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string sourceName = Path.GetFileName(source);
        string prompt = string.IsNullOrEmpty(sourceName)
            ? "Rename to:"
            : $"Rename {sourceName} to:";
        return Show("Rename", prompt, "Rename", initialDestination, initialOptions, MoveConflictModes, copyModes: null, showOperationOptions: false);
    }

    private FileOperationDialogResult? Show(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        IReadOnlyList<CopyMode>? copyModes,
        bool showOperationOptions)
    {
        return RunLoop(title, prompt, actionLabel, initialDestination, initialOptions, conflictModes, copyModes, showOperationOptions);
    }

    private FileOperationDialogResult? RunLoop(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        IReadOnlyList<CopyMode>? copyModes,
        bool showOperationOptions)
    {
        var destination = new CommandLineState();
        destination.SetText(initialDestination);
        var filter = new CommandLineState();
        filter.SetText(string.IsNullOrWhiteSpace(initialOptions.FileMask) ? "*" : initialOptions.FileMask);

        SingleLineTextHistoryState destinationHistory = HistoryRegistry.GetOrCreate("FileOperationDialog.Destination");
        SingleLineTextHistoryState filterHistory = HistoryRegistry.GetOrCreate("FileOperationDialog.Filter");
        var destinationRowState = new TextInputRowState();
        var filterRowState = new TextInputRowState();

        var securityChoice = new ChoiceFormRow<FileSecurityMode>(
            new ChoiceRow<FileSecurityMode>(
                SecurityModes,
                SecurityModeLabel,
                Array.IndexOf(SecurityModes, initialOptions.SecurityMode) is var securityIndex && securityIndex >= 0 ? securityIndex : 0),
            "Access rights:")
        {
            Id = "security",
        };
        ChoiceFormRow<CopyMode>? copyModeChoice = copyModes is null
            ? null
            : new ChoiceFormRow<CopyMode>(
                new ChoiceRow<CopyMode>(
                    copyModes,
                    CopyModeLabel,
                    FindCopyModeIndex(initialOptions.CopyMode, copyModes)),
                "Copy mode:")
            {
                Id = "copyMode",
            };
        var conflictChoice = new ChoiceRow<ConflictDecisionMode>(
            conflictModes,
            ConflictLabel,
            FindConflictIndex(initialOptions.DefaultConflictDecision, conflictModes));
        var conflictChoiceRow = new MultiLineChoiceFormRow<ConflictDecisionMode>(
            conflictChoice,
            string.Empty,
            [Math.Min(4, conflictModes.Count), conflictModes.Count])
        {
            Id = "conflict",
        };
        var preserveTimestamps = new CheckBoxRow(new CheckBoxLine("Preserve all timestamps", initialOptions.PreserveTimestamps))
        {
            Id = "preserveTimestamps",
        };
        var preserveAttributes = new CheckBoxRow(new CheckBoxLine("Preserve attributes", initialOptions.PreserveAttributes))
        {
            Id = "preserveAttributes",
        };
        var copySymlinkContents = new CheckBoxRow(new CheckBoxLine(
            "Copy contents of symbolic links",
            initialOptions.SymlinkMode == SymlinkCopyMode.CopyTargetContents))
        {
            Id = "copySymlinkContents",
        };
        var useFilter = new CheckBoxRow(new CheckBoxLine("Use filter", !string.IsNullOrWhiteSpace(initialOptions.FileMask)))
        {
            Id = "useFilter",
        };
        var buttons = new ButtonRow(
            [
                new DialogButton("submit", actionLabel, actionLabel[0], IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ],
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput)
        {
            Id = "footerButtons",
        };
        var form = new ScrollableFormDialog();
        string? error = null;

        void PrepareRows() =>
            form.SetRows(BuildRows(
                prompt,
                destination,
                filter,
                destinationHistory,
                filterHistory,
                destinationRowState,
                filterRowState,
                securityChoice,
                copyModeChoice,
                conflictChoiceRow,
                preserveTimestamps,
                preserveAttributes,
                copySymlinkContents,
                useFilter,
                showOperationOptions),
                footerRows: [buttons]);

        return _modalDialogs.Run(
            context => Draw(context, title, form, error),
            input =>
            {
            FormInputResult result = input switch
            {
                KeyConsoleInputEvent { Key: var key } => HandleOperationKey(form, key),
                MouseConsoleInputEvent mouse => form.HandleMouse(mouse),
                _ => FormInputResult.NotHandled,
            };

            if (result.Kind == FormInputResultKind.Cancel)
                return ModalDialogLoopResult<FileOperationDialogResult?>.Complete(null);

            if (result.Kind == FormInputResultKind.Submit ||
                input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
            {
                var dialogResult = BuildResult(
                    destination,
                    filter,
                    initialOptions,
                    conflictChoice.Value,
                    copyModeChoice?.Value ?? CopyMode.Normal,
                    securityChoice.Value,
                    preserveTimestamps.Value,
                    preserveAttributes.Value,
                    copySymlinkContents.Value,
                    useFilter.Value,
                    destinationHistory,
                    filterHistory,
                    ref error);
                if (dialogResult is not null)
                    return ModalDialogLoopResult<FileOperationDialogResult?>.Complete(dialogResult);
            }

            return ModalDialogLoopResult<FileOperationDialogResult?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildRows(
        string prompt,
        CommandLineState destination,
        CommandLineState filter,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory,
        TextInputRowState destinationRowState,
        TextInputRowState filterRowState,
        ChoiceFormRow<FileSecurityMode> securityChoice,
        ChoiceFormRow<CopyMode>? copyModeChoice,
        MultiLineChoiceFormRow<ConflictDecisionMode> conflictChoiceRow,
        CheckBoxRow preserveTimestamps,
        CheckBoxRow preserveAttributes,
        CheckBoxRow copySymlinkContents,
        CheckBoxRow useFilter,
        bool showOperationOptions)
    {
        var fill = FarDialogStyles.Fill;
        var rows = new List<IFormRow>
        {
            new LabelRow(prompt, fill),
            new TextInputRow(destination, destinationHistory, destinationRowState)
            {
                Id = "destination",
                SubmitOnEnter = true,
            },
            new SeparatorRow(fill, drawLine: false),
        };

        if (showOperationOptions)
        {
            if (copyModeChoice is not null)
            {
                rows.Add(copyModeChoice);
                rows.Add(new SeparatorRow(fill, drawLine: false));
            }

            rows.Add(securityChoice);
            rows.Add(new SeparatorRow(fill, drawLine: false));
        }

        rows.Add(new LabelRow("Already existing files:", fill));
        rows.Add(conflictChoiceRow);

        if (showOperationOptions)
        {
            rows.Add(new SeparatorRow(fill, drawLine: false));
            rows.Add(preserveTimestamps);
            rows.Add(preserveAttributes);
            rows.Add(copySymlinkContents);
            rows.Add(new SeparatorRow(fill, drawLine: false));
            rows.Add(useFilter);
            rows.Add(new LabelRow("Filter mask:", fill));
            rows.Add(useFilter.Value
                ? new TextInputRow(filter, filterHistory, filterRowState)
                {
                    Id = "filter",
                    SubmitOnEnter = true,
                }
                : new LabelRow(SingleLineTextInput.VisibleText(filter, 60), fill) { Id = "filter" });
            rows.Add(new SeparatorRow(fill, drawLine: false));
        }

        return rows;
    }

    private static FormInputResult HandleOperationKey(
        ScrollableFormDialog form,
        ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.F10)
            return FormInputResult.Submit("submit");

        FormInputResult formResult = form.HandleKey(key);
        if (formResult.IsHandled)
            return formResult;

        return key.Key == ConsoleKey.Enter && form.IsFocusedOnSubmitRow
            ? FormInputResult.Submit("submit")
            : FormInputResult.NotHandled;
    }

    private static FileOperationDialogResult? BuildResult(
        CommandLineState destination,
        CommandLineState filter,
        FileOperationOptions initialOptions,
        ConflictDecisionMode conflictMode,
        CopyMode copyMode,
        FileSecurityMode securityMode,
        bool preserveTimestamps,
        bool preserveAttributes,
        bool copySymlinkContents,
        bool useFilter,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory,
        ref string? error)
    {
        string destinationText = destination.Text.Trim();
        if (string.IsNullOrWhiteSpace(destinationText))
        {
            error = "Destination must not be empty.";
            return null;
        }

        error = null;
        string? mask = useFilter && !string.IsNullOrWhiteSpace(filter.Text)
            ? filter.Text.Trim()
            : null;

        destinationHistory.Add(destinationText);
        if (mask is not null)
            filterHistory.Add(mask);
        destinationHistory.Close();
        filterHistory.Close();

        return new FileOperationDialogResult(
            destinationText,
            initialOptions with
            {
                DefaultConflictDecision = conflictMode,
                CopyMode = copyMode,
                SecurityMode = securityMode,
                PreserveTimestamps = preserveTimestamps,
                PreserveAttributes = preserveAttributes,
                SymlinkMode = copySymlinkContents ? SymlinkCopyMode.CopyTargetContents : SymlinkCopyMode.CopyLink,
                FileMask = mask,
            });
    }

    private void Draw(UiRenderContext context, string title, ScrollableFormDialog form, string? error)
    {
        Rect outerBounds = OuterBounds(context.Size);

        _modalRenderer.Render(context.Screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int buttonY = bounds.Y + bounds.Height - 2;
            int errorY = buttonY - 1;
            int bodyTop = bounds.Y + 1;
            int bodyHeight = Math.Max(1, errorY - bodyTop);

            form.Render(new FormRenderContext(
                context,
                new Rect(contentX, bodyTop, contentWidth, bodyHeight),
                FarDialogStyles.Border,
                new Rect(contentX, buttonY, contentWidth, 1)));

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            context.Screen.Write(contentX, errorY, errorText.PadRight(contentWidth), FarDialogStyles.Error);
        });
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }

    private static int FindConflictIndex(ConflictDecisionMode mode, IReadOnlyList<ConflictDecisionMode> conflictModes)
    {
        for (int i = 0; i < conflictModes.Count; i++)
        {
            if (conflictModes[i] == mode)
                return i;
        }

        return 0;
    }

    private static int FindCopyModeIndex(CopyMode mode, IReadOnlyList<CopyMode> copyModes)
    {
        for (int i = 0; i < copyModes.Count; i++)
        {
            if (copyModes[i] == mode)
                return i;
        }

        return 0;
    }

    private static string ConflictLabel(ConflictDecisionMode mode) => mode switch
    {
        ConflictDecisionMode.Overwrite => "Overwrite",
        ConflictDecisionMode.OverwriteAll => "Overwrite all",
        ConflictDecisionMode.Skip => "Skip",
        ConflictDecisionMode.SkipAll => "Skip all",
        ConflictDecisionMode.Rename => "Rename",
        ConflictDecisionMode.RenameAll => "Rename all",
        ConflictDecisionMode.OnlyNewer => "Only newer",
        _ => "Ask",
    };

    private static string CopyModeLabel(CopyMode mode) => mode switch
    {
        CopyMode.Reliable => "Reliable",
        CopyMode.FastSalvage => "Fast salvage",
        _ => "Normal",
    };

    private static string SecurityModeLabel(FileSecurityMode mode) => mode switch
    {
        FileSecurityMode.CopyAccessControl => "Copy",
        FileSecurityMode.Inherit => "Inherit",
        _ => "Default",
    };

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }
}
