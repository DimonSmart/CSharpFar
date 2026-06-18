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
    private const int DialogHeight = 22;

    private static readonly ConflictDecisionMode[] CopyConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
        ConflictDecisionMode.Append,
        ConflictDecisionMode.ResumeWithTailValidation,
    ];

    private static readonly ConflictDecisionMode[] MoveConflictModes =
    [
        ConflictDecisionMode.Ask,
        ConflictDecisionMode.Overwrite,
        ConflictDecisionMode.Skip,
        ConflictDecisionMode.Rename,
        ConflictDecisionMode.OnlyNewer,
    ];

    private static readonly FileSecurityMode[] SecurityModes =
    [
        FileSecurityMode.Default,
        FileSecurityMode.CopyAccessControl,
        FileSecurityMode.Inherit,
    ];

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FileOperationDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public FileOperationDialogResult? ShowCopy(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? $"Copy {Path.GetFileName(sources[0])} to:"
            : $"Copy {sources.Count} items to:";
        return Show("Copy", prompt, "Copy", initialDestination, initialOptions, CopyConflictModes, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowMove(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string prompt = sources.Count == 1
            ? "Move / Rename to:"
            : $"Move {sources.Count} items to:";
        return Show("Move", prompt, "Move", initialDestination, initialOptions, MoveConflictModes, showOperationOptions: true);
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
        return Show("Rename", prompt, "Rename", initialDestination, initialOptions, MoveConflictModes, showOperationOptions: false);
    }

    private FileOperationDialogResult? Show(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        bool showOperationOptions)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, title, prompt, actionLabel, initialDestination, initialOptions, conflictModes, showOperationOptions);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private FileOperationDialogResult? RunLoop(
        ConsoleSize size,
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
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
            FarDialogStyles.FocusedInput);
        var form = new ScrollableFormDialog();
        string? error = null;
        bool footerFocused = false;

        while (true)
        {
            form.SetRows(BuildRows(
                prompt,
                destination,
                filter,
                destinationHistory,
                filterHistory,
                destinationRowState,
                filterRowState,
                securityChoice,
                conflictChoiceRow,
                preserveTimestamps,
                copySymlinkContents,
                useFilter,
                showOperationOptions));
            Draw(size, title, form, buttons, error, footerFocused);

            var input = _screen.ReadInput();
            string lastBodyRowId = showOperationOptions
                ? useFilter.Value ? "filter" : "useFilter"
                : "conflict";
            FormInputResult result = input switch
            {
                KeyConsoleInputEvent { Key: var key } => HandleOperationKey(form, buttons, lastBodyRowId, key, ref footerFocused),
                MouseConsoleInputEvent mouse => HandleOperationMouse(form, buttons, mouse, ref footerFocused),
                _ => FormInputResult.NotHandled,
            };

            if (result.Kind == FormInputResultKind.Cancel)
                return null;

            if (result.Kind == FormInputResultKind.Submit ||
                input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 })
            {
                var dialogResult = BuildResult(
                    destination,
                    filter,
                    initialOptions,
                    conflictChoice.Value,
                    securityChoice.Value,
                    preserveTimestamps.Value,
                    copySymlinkContents.Value,
                    useFilter.Value,
                    destinationHistory,
                    filterHistory,
                    ref error);
                if (dialogResult is not null)
                    return dialogResult;
            }
        }
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
        MultiLineChoiceFormRow<ConflictDecisionMode> conflictChoiceRow,
        CheckBoxRow preserveTimestamps,
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
            rows.Add(securityChoice);
            rows.Add(new SeparatorRow(fill, drawLine: false));
        }

        rows.Add(new LabelRow("Already existing files:", fill));
        rows.Add(conflictChoiceRow);

        if (showOperationOptions)
        {
            rows.Add(new SeparatorRow(fill, drawLine: false));
            rows.Add(preserveTimestamps);
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
        ButtonRow buttons,
        string lastBodyRowId,
        ConsoleKeyInfo key,
        ref bool footerFocused)
    {
        if (key.Key == ConsoleKey.F10)
            return FormInputResult.Submit("submit");

        bool shiftTab = key.Key == ConsoleKey.Tab && (key.Modifiers & ConsoleModifiers.Shift) != 0;
        if (footerFocused)
        {
            if (shiftTab || key.Key == ConsoleKey.Tab)
            {
                footerFocused = false;
                return FormInputResult.Handled;
            }

            return buttons.HandleKey(key, new FormRowInputContext(form.FocusableCount, focused: true));
        }

        if (key.Key == ConsoleKey.Tab &&
            !shiftTab &&
            form.FocusableCount > 0 &&
            form.IsFocused(lastBodyRowId))
        {
            footerFocused = true;
            return FormInputResult.Handled;
        }

        FormInputResult formResult = form.HandleKey(key);
        if (formResult.IsHandled)
            return formResult;

        return key.Key == ConsoleKey.Enter
            ? FormInputResult.Submit("submit")
            : FormInputResult.NotHandled;
    }

    private static FormInputResult HandleOperationMouse(
        ScrollableFormDialog form,
        ButtonRow buttons,
        MouseConsoleInputEvent mouse,
        ref bool footerFocused)
    {
        FormInputResult buttonResult = buttons.HandleMouse(mouse, new FormRowMouseContext(default, 0, focused: footerFocused, screenHeight: 0));
        if (buttonResult.IsHandled)
        {
            footerFocused = true;
            return buttonResult;
        }

        FormInputResult formResult = form.HandleMouse(mouse);
        if (formResult.IsHandled)
            footerFocused = false;

        return formResult;
    }

    private static FileOperationDialogResult? BuildResult(
        CommandLineState destination,
        CommandLineState filter,
        FileOperationOptions initialOptions,
        ConflictDecisionMode conflictMode,
        FileSecurityMode securityMode,
        bool preserveTimestamps,
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
                SecurityMode = securityMode,
                PreserveTimestamps = preserveTimestamps,
                SymlinkMode = copySymlinkContents ? SymlinkCopyMode.CopyTargetContents : SymlinkCopyMode.CopyLink,
                FileMask = mask,
            });
    }

    private void Draw(ConsoleSize size, string title, ScrollableFormDialog form, ButtonRow buttons, string? error, bool footerFocused)
    {
        using var frame = _screen.BeginFrame();
        Rect outerBounds = OuterBounds(size);

        _modalRenderer.Render(_screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int buttonY = bounds.Y + bounds.Height - 2;
            int errorY = buttonY - 1;
            int bodyTop = bounds.Y + 1;
            int bodyHeight = Math.Max(1, errorY - bodyTop);

            form.Render(new FormRenderContext(
                _screen,
                new Rect(contentX, bodyTop, contentWidth, bodyHeight),
                FarDialogStyles.Border));

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, errorY, errorText.PadRight(contentWidth), FarDialogStyles.Error);
            buttons.Render(new FormRowRenderContext(
                _screen,
                new Rect(contentX, buttonY, contentWidth, 1),
                focused: footerFocused));

            if (footerFocused)
                _screen.SetCursorVisible(false);
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

    private static string ConflictLabel(ConflictDecisionMode mode) => mode switch
    {
        ConflictDecisionMode.Overwrite => "Overwrite",
        ConflictDecisionMode.OverwriteAll => "Overwrite all",
        ConflictDecisionMode.Skip => "Skip",
        ConflictDecisionMode.SkipAll => "Skip all",
        ConflictDecisionMode.Rename => "Rename",
        ConflictDecisionMode.RenameAll => "Rename all",
        ConflictDecisionMode.Append => "Append",
        ConflictDecisionMode.AppendAll => "Append all",
        ConflictDecisionMode.ResumeWithTailValidation => "Paranoid",
        ConflictDecisionMode.OnlyNewer => "Only newer",
        _ => "Ask",
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
