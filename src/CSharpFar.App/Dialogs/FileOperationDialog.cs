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
    private const int BodyRowCount = 16;

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

    private readonly ScreenRenderer _screen;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly CheckBoxLine _preserveTimestamps = new("Preserve all timestamps");
    private readonly CheckBoxLine _copySymlinkContents = new("Copy contents of symbolic links");
    private readonly CheckBoxLine _useFilter = new("Use filter");

    public FileOperationDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public FileOperationDialogResult? ShowCopy(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string title = "Copy";
        string prompt = sources.Count == 1
            ? $"Copy {Path.GetFileName(sources[0])} to:"
            : $"Copy {sources.Count} items to:";
        return Show(title, prompt, "Copy", initialDestination, initialOptions, CopyConflictModes, showOperationOptions: true);
    }

    public FileOperationDialogResult? ShowMove(
        IReadOnlyList<string> sources,
        string initialDestination,
        FileOperationOptions initialOptions)
    {
        string title = "Move";
        string prompt = sources.Count == 1
            ? "Move / Rename to:"
            : $"Move {sources.Count} items to:";
        return Show(title, prompt, "Move", initialDestination, initialOptions, MoveConflictModes, showOperationOptions: true);
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
        if (!string.IsNullOrWhiteSpace(initialOptions.FileMask))
            filter.SetText(initialOptions.FileMask);
        else
            filter.SetText("*");
        SingleLineTextHistoryState destinationHistory = HistoryRegistry.GetOrCreate("FileOperationDialog.Destination");
        SingleLineTextHistoryState filterHistory = HistoryRegistry.GetOrCreate("FileOperationDialog.Filter");

        int conflictIndex = FindConflictIndex(initialOptions.DefaultConflictDecision, conflictModes);
        var securityMode = initialOptions.SecurityMode;
        bool preserveTimestamps = initialOptions.PreserveTimestamps;
        bool copySymlinkContents = initialOptions.SymlinkMode == SymlinkCopyMode.CopyTargetContents;
        bool useFilter = !string.IsNullOrWhiteSpace(initialOptions.FileMask);
        int focusRow = 0;
        int bodyScrollTop = 0;
        ScrollBarDragState? bodyScrollbarDrag = null;
        ScrollBarDragState? historyScrollbarDrag = null;
        int focusedButton = 0;
        string? error = null;
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("submit", actionLabel, actionLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

        bodyScrollTop = NormalizeBodyScroll(size, focusRow, bodyScrollTop);
        Draw(
            size,
            title,
            prompt,
            actionLabel,
            destination,
            filter,
            destinationHistory,
            filterHistory,
            conflictModes,
            conflictIndex,
            securityMode,
            preserveTimestamps,
            copySymlinkContents,
            useFilter,
            showOperationOptions,
            focusRow,
            bodyScrollTop,
            buttonBar,
            focusedButton,
            error);

        while (true)
        {
            var input = _screen.ReadInput();

            if (focusRow == 7 &&
                buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId is not null)
                {
                    if (buttonId == "cancel")
                        return null;

                    var result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                        securityMode, preserveTimestamps, copySymlinkContents, useFilter, destinationHistory, filterHistory, ref error);
                    if (result is not null)
                        return result;
                }

                Draw(
                    size,
                    title,
                    prompt,
                    actionLabel,
                    destination,
                    filter,
                    destinationHistory,
                    filterHistory,
                    conflictModes,
                    conflictIndex,
                    securityMode,
                    preserveTimestamps,
                    copySymlinkContents,
                    useFilter,
                    showOperationOptions,
                    focusRow,
                    bodyScrollTop,
                    buttonBar,
                    focusedButton,
                    error);
                continue;
            }

            if (input is MouseConsoleInputEvent dropdownMouse &&
                TryHandleHistoryDropdownMouse(
                    dropdownMouse,
                    size,
                    bodyScrollTop,
                    destination,
                    filter,
                    destinationHistory,
                    filterHistory,
                    ref historyScrollbarDrag))
            {
                DrawCurrent(ensureFocusVisible: false);
                continue;
            }

            if (input is MouseConsoleInputEvent mouse &&
                TryHandleBodyScrollbarMouse(mouse, size, ref bodyScrollTop, ref bodyScrollbarDrag))
            {
                DrawCurrent(ensureFocusVisible: false);
                continue;
            }

            if (input is MouseConsoleInputEvent historyMouse &&
                TryHandleHistoryArrow(historyMouse, size, bodyScrollTop, destinationHistory, filterHistory, out int historyFocusRow))
            {
                focusRow = historyFocusRow;
                DrawCurrent();
                continue;
            }

            if (input is MouseConsoleInputEvent checkboxMouse && showOperationOptions)
            {
                if (_preserveTimestamps.TryHandleMouse(checkboxMouse))
                {
                    focusRow = 3;
                    preserveTimestamps = _preserveTimestamps.Value;
                    DrawCurrent();
                    continue;
                }

                if (_copySymlinkContents.TryHandleMouse(checkboxMouse))
                {
                    focusRow = 4;
                    copySymlinkContents = _copySymlinkContents.Value;
                    DrawCurrent();
                    continue;
                }

                if (_useFilter.TryHandleMouse(checkboxMouse))
                {
                    focusRow = 5;
                    useFilter = _useFilter.Value;
                    DrawCurrent();
                    continue;
                }
            }

            if (input is MouseConsoleInputEvent bodyMouse &&
                TryHandleBodyMouse(
                    bodyMouse,
                    size,
                    bodyScrollTop,
                    conflictModes,
                    showOperationOptions,
                    ref focusRow,
                    ref conflictIndex,
                    ref securityMode,
                    ref preserveTimestamps,
                    ref copySymlinkContents,
                    ref useFilter))
            {
                DrawCurrent();
                continue;
            }

            if (input is MouseConsoleInputEvent &&
                buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                buttonId is not null)
            {
                focusRow = 7;
                if (buttonId == "cancel")
                    return null;

                var result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                    securityMode, preserveTimestamps, copySymlinkContents, useFilter, destinationHistory, filterHistory, ref error);
                if (result is not null)
                    return result;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                DrawCurrent();
                continue;
            }

            if (focusRow is 0 or 6 &&
                CurrentHistory(focusRow, destinationHistory, filterHistory).IsDropdownOpen &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                EditText(
                    focusRow == 0 ? destination : filter,
                    key,
                    CurrentHistory(focusRow, destinationHistory, filterHistory),
                    DropdownRows(size, focusRow, bodyScrollTop),
                    ref error);
                DrawCurrent();
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    var f10Result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                        securityMode, preserveTimestamps, copySymlinkContents, useFilter, destinationHistory, filterHistory, ref error);
                    if (f10Result is not null)
                        return f10Result;
                    break;
                case ConsoleKey.Enter:
                    if (focusRow is 0 or 6 or 7)
                    {
                        if (focusRow == 7 && focusedButton == 1)
                            return null;

                        var result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                            securityMode, preserveTimestamps, copySymlinkContents, useFilter, destinationHistory, filterHistory, ref error);
                        if (result is not null)
                            return result;
                    }
                    else
                    {
                        CycleFocusedValue(focusRow, conflictModes, ref conflictIndex, ref securityMode, ref preserveTimestamps,
                            ref copySymlinkContents, ref useFilter);
                    }
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == 7)
                    {
                        if (focusedButton == 1)
                            return null;

                        var result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                            securityMode, preserveTimestamps, copySymlinkContents, useFilter, destinationHistory, filterHistory, ref error);
                        if (result is not null)
                            return result;
                    }
                    else if (focusRow is not 0 and not 6)
                    {
                        CycleFocusedValue(focusRow, conflictModes, ref conflictIndex, ref securityMode, ref preserveTimestamps,
                            ref copySymlinkContents, ref useFilter);
                    }
                    else
                    {
                        EditText(
                            focusRow == 0 ? destination : filter,
                            key,
                            CurrentHistory(focusRow, destinationHistory, filterHistory),
                            DropdownRows(size, focusRow, bodyScrollTop),
                            ref error);
                    }
                    break;
                case ConsoleKey.LeftArrow:
                    if (focusRow == 7)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow == 2)
                        conflictIndex = conflictIndex <= 0 ? conflictModes.Count - 1 : conflictIndex - 1;
                    else if (focusRow is 0 or 6)
                        EditText(
                            focusRow == 0 ? destination : filter,
                            key,
                            CurrentHistory(focusRow, destinationHistory, filterHistory),
                            DropdownRows(size, focusRow, bodyScrollTop),
                            ref error);
                    break;
                case ConsoleKey.RightArrow:
                    if (focusRow == 7)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow == 2)
                        conflictIndex = (conflictIndex + 1) % conflictModes.Count;
                    else if (focusRow is 0 or 6)
                        EditText(
                            focusRow == 0 ? destination : filter,
                            key,
                            CurrentHistory(focusRow, destinationHistory, filterHistory),
                            DropdownRows(size, focusRow, bodyScrollTop),
                            ref error);
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = PreviousFocusableRow(focusRow, useFilter, showOperationOptions);
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                    focusRow = NextFocusableRow(focusRow, useFilter, showOperationOptions);
                    break;
                default:
                    if (focusRow is 0 or 6)
                        EditText(
                            focusRow == 0 ? destination : filter,
                            key,
                            CurrentHistory(focusRow, destinationHistory, filterHistory),
                            DropdownRows(size, focusRow, bodyScrollTop),
                            ref error);
                    break;
            }

            if (!useFilter && focusRow == 6)
                focusRow = 5;

            DrawCurrent();
        }

        void DrawCurrent(bool ensureFocusVisible = true)
        {
            bodyScrollTop = ensureFocusVisible
                ? NormalizeBodyScroll(size, focusRow, bodyScrollTop)
                : ScrollStateCalculator.ClampFirstVisibleIndex(
                    bodyScrollTop,
                    BodyRowCount,
                    FileOperationBodyViewportRows(size));
            Draw(
                size,
                title,
                prompt,
                actionLabel,
                destination,
                filter,
                destinationHistory,
                filterHistory,
                conflictModes,
                conflictIndex,
                securityMode,
                preserveTimestamps,
                copySymlinkContents,
                useFilter,
                showOperationOptions,
                focusRow,
                bodyScrollTop,
                buttonBar,
                focusedButton,
                error);
        }
    }

    private static bool TryHandleBodyScrollbarMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var frameBounds = new Rect(
            dialogX + 1,
            dialogY + 1,
            Math.Max(1, dialogWidth - 2),
            Math.Max(1, dialogHeight - 2));
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        int errorY = buttonY - 1;
        int bodyTop = frameBounds.Y + 1;
        int bodyHeight = Math.Max(1, errorY - bodyTop);
        if (BodyRowCount <= bodyHeight)
            return false;

        var scrollbarBounds = new Rect(frameBounds.Right - 1, bodyTop, 1, bodyHeight);
        return ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            scrollbarBounds,
            BodyRowCount,
            bodyHeight,
            ref bodyScrollTop,
            ref bodyScrollbarDrag);
    }

    private static bool TryHandleBodyMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int bodyScrollTop,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        bool showOperationOptions,
        ref int focusRow,
        ref int conflictIndex,
        ref FileSecurityMode securityMode,
        ref bool preserveTimestamps,
        ref bool copySymlinkContents,
        ref bool useFilter)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        if (!TryGetBodyMousePosition(size, bodyScrollTop, mouse.X, mouse.Y, out int virtualRow, out int contentColumn))
            return false;

        switch (virtualRow)
        {
            case 1:
                focusRow = 0;
                return true;
            case 3:
                if (!showOperationOptions)
                    return false;
                focusRow = 1;
                if (TryHitAccessRights(contentColumn, out FileSecurityMode selectedSecurityMode))
                    securityMode = selectedSecurityMode;
                return true;
            case 5:
                focusRow = 2;
                return true;
            case 6:
                focusRow = 2;
                if (TryHitConflictMode(contentColumn, conflictModes, startIndex: 0, Math.Min(4, conflictModes.Count), out int topConflictIndex))
                    conflictIndex = topConflictIndex;
                return true;
            case 7:
                focusRow = 2;
                if (TryHitConflictMode(contentColumn, conflictModes, startIndex: 4, conflictModes.Count, out int bottomConflictIndex))
                    conflictIndex = bottomConflictIndex;
                return true;
            case 9:
                if (!showOperationOptions)
                    return false;
                focusRow = 3;
                preserveTimestamps = !preserveTimestamps;
                return true;
            case 10:
                if (!showOperationOptions)
                    return false;
                focusRow = 4;
                copySymlinkContents = !copySymlinkContents;
                return true;
            case 12:
                if (!showOperationOptions)
                    return false;
                focusRow = 5;
                useFilter = !useFilter;
                return true;
            case 14:
                if (!showOperationOptions)
                    return false;
                if (!useFilter)
                    return false;
                focusRow = 6;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetBodyMousePosition(
        ConsoleSize size,
        int bodyScrollTop,
        int mouseX,
        int mouseY,
        out int virtualRow,
        out int contentColumn)
    {
        virtualRow = -1;
        contentColumn = -1;

        Rect frameBounds = FrameBounds(size);
        int contentX = frameBounds.X + 2;
        int contentWidth = Math.Max(1, frameBounds.Width - 4);
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        int errorY = buttonY - 1;
        int bodyTop = frameBounds.Y + 1;
        int bodyHeight = Math.Max(1, errorY - bodyTop);

        if (mouseX < contentX || mouseX >= contentX + contentWidth ||
            mouseY < bodyTop || mouseY >= bodyTop + bodyHeight)
        {
            return false;
        }

        virtualRow = bodyScrollTop + mouseY - bodyTop;
        contentColumn = mouseX - contentX;
        return virtualRow >= 0 && virtualRow < BodyRowCount;
    }

    private static bool TryHitAccessRights(int contentColumn, out FileSecurityMode securityMode)
    {
        const string prefix = "Access rights: ";
        int column = prefix.Length;
        foreach ((FileSecurityMode mode, string label) in AccessRightLabels())
        {
            if (contentColumn >= column && contentColumn < column + label.Length)
            {
                securityMode = mode;
                return true;
            }

            column += label.Length + 1;
        }

        securityMode = FileSecurityMode.Default;
        return false;
    }

    private static bool TryHitConflictMode(
        int contentColumn,
        IReadOnlyList<ConflictDecisionMode> modes,
        int startIndex,
        int endIndex,
        out int conflictIndex)
    {
        int column = 0;
        for (int i = startIndex; i < endIndex; i++)
        {
            string label = $"( ) {ConflictLabel(modes[i])}";
            if (contentColumn >= column && contentColumn < column + label.Length)
            {
                conflictIndex = i;
                return true;
            }

            column += label.Length + 1;
        }

        conflictIndex = -1;
        return false;
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
        if (destinationText.Length == 0)
        {
            error = "Destination is required.";
            return null;
        }

        error = null;
        string? mask = useFilter && !string.IsNullOrWhiteSpace(filter.Text)
            ? filter.Text.Trim()
            : null;

        destinationHistory.Add(destinationText);
        if (mask is not null)
            filterHistory.Add(mask);

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

    private static SingleLineTextHistoryState CurrentHistory(
        int focusRow,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory) =>
        focusRow == 0 ? destinationHistory : filterHistory;

    private static void EditText(
        CommandLineState buffer,
        ConsoleKeyInfo key,
        SingleLineTextHistoryState history,
        int availableRows,
        ref string? error)
    {
        SingleLineTextInput.HandleKey(buffer, key, ref error, history, availableRows);
    }

    private void CycleFocusedValue(
        int focusRow,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        ref int conflictIndex,
        ref FileSecurityMode securityMode,
        ref bool preserveTimestamps,
        ref bool copySymlinkContents,
        ref bool useFilter)
    {
        switch (focusRow)
        {
            case 1:
                securityMode = securityMode switch
                {
                    FileSecurityMode.Default => FileSecurityMode.CopyAccessControl,
                    FileSecurityMode.CopyAccessControl => FileSecurityMode.Inherit,
                    _ => FileSecurityMode.Default,
                };
                break;
            case 2:
                conflictIndex = (conflictIndex + 1) % conflictModes.Count;
                break;
            case 3:
                _preserveTimestamps.Value = preserveTimestamps;
                _preserveTimestamps.TryHandleKey(ToggleKey());
                preserveTimestamps = _preserveTimestamps.Value;
                break;
            case 4:
                _copySymlinkContents.Value = copySymlinkContents;
                _copySymlinkContents.TryHandleKey(ToggleKey());
                copySymlinkContents = _copySymlinkContents.Value;
                break;
            case 5:
                _useFilter.Value = useFilter;
                _useFilter.TryHandleKey(ToggleKey());
                useFilter = _useFilter.Value;
                break;
        }
    }

    private static ConsoleKeyInfo ToggleKey() =>
        new('\0', ConsoleKey.Spacebar, shift: false, alt: false, control: false);

    private static int NextFocusableRow(int focusRow, bool useFilter, bool showOperationOptions)
    {
        if (!showOperationOptions)
        {
            if (focusRow == 0)
                return 2;
            if (focusRow == 2)
                return 7;
            return 0;
        }

        if (focusRow == 5 && useFilter)
            return 6;
        if (focusRow == 5 || focusRow == 6)
            return 7;
        if (focusRow >= 7)
            return 0;
        return focusRow + 1;
    }

    private static int PreviousFocusableRow(int focusRow, bool useFilter, bool showOperationOptions)
    {
        if (!showOperationOptions)
        {
            if (focusRow == 0)
                return 7;
            if (focusRow == 7)
                return 2;
            return 0;
        }

        if (focusRow == 0)
            return 7;
        if (focusRow == 7)
            return useFilter ? 6 : 5;
        if (focusRow == 6)
            return 5;
        return focusRow - 1;
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

    private static IReadOnlyList<(FileSecurityMode Mode, string Label)> AccessRightLabels() =>
    [
        (FileSecurityMode.Default, "( ) Default"),
        (FileSecurityMode.CopyAccessControl, "( ) Copy"),
        (FileSecurityMode.Inherit, "( ) Inherit"),
    ];

    private void Draw(
        ConsoleSize size,
        string title,
        string prompt,
        string actionLabel,
        CommandLineState destination,
        CommandLineState filter,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        int conflictIndex,
        FileSecurityMode securityMode,
        bool preserveTimestamps,
        bool copySymlinkContents,
        bool useFilter,
        bool showOperationOptions,
        int focusRow,
        int bodyScrollTop,
        DialogButtonBar buttonBar,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        var fill = FarDialogStyles.Fill;
        var focused = FarDialogStyles.FocusedInput;

        _modalRenderer.Render(_screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            int buttonY = bounds.Y + bounds.Height - 2;
            int errorY = buttonY - 1;
            int bodyTop = bounds.Y + 1;
            int bodyHeight = Math.Max(1, errorY - bodyTop);
            _screen.FillRegion(new Rect(contentX, bodyTop, contentWidth, bodyHeight), fill);

            WriteBodyRow(0, prompt, fill);
            DrawBodyInput(1, destination, destinationHistory, focusRow == 0);
            DrawBodySeparator(2);
            if (showOperationOptions)
            {
                DrawBodyAccessRights(3);
                DrawBodySeparator(4);
            }
            WriteBodyRow(5, "Already existing files:", fill);
            DrawBodyConflictModeRow(6, 0, Math.Min(4, conflictModes.Count));
            DrawBodyConflictModeRow(7, 4, conflictModes.Count);
            if (showOperationOptions)
            {
                DrawBodySeparator(8);
                DrawBodyCheckbox(9, "Preserve all timestamps", preserveTimestamps, focusRow == 3);
                DrawBodyCheckbox(10, "Copy contents of symbolic links", copySymlinkContents, focusRow == 4);
                DrawBodySeparator(11);
                DrawBodyCheckbox(12, "Use filter", useFilter, focusRow == 5);
                WriteBodyRow(13, "Filter mask:", fill);
                if (useFilter)
                    DrawBodyInput(14, filter, filterHistory, focusRow == 6);
                else
                    WriteBodyRow(14, SingleLineTextInput.VisibleText(filter, contentWidth), fill);
                DrawBodySeparator(15);
            }

            if (BodyRowCount > bodyHeight)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    _screen,
                    new Rect(bounds.Right - 1, bodyTop, 1, bodyHeight),
                    new ScrollState
                    {
                        TotalItems = BodyRowCount,
                        ViewportItems = bodyHeight,
                        FirstVisibleIndex = bodyScrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    FarDialogStyles.Border);
            }

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, errorY, errorText.PadRight(contentWidth), FarDialogStyles.Error);

            buttonBar.Render(
                _screen,
                contentX,
                buttonY,
                contentWidth,
                focusedButton,
                fill,
                focusRow == 7 ? focused : fill);

            int? BodyY(int virtualRow)
            {
                int row = virtualRow - bodyScrollTop;
                return row >= 0 && row < bodyHeight ? bodyTop + row : null;
            }

            void WriteBodyRow(int virtualRow, string value, CellStyle style)
            {
                if (BodyY(virtualRow) is { } y)
                    _screen.Write(contentX, y, Truncate(value, contentWidth).PadRight(contentWidth), style);
            }

            void DrawBodyInput(
                int virtualRow,
                CommandLineState buffer,
                SingleLineTextHistoryState history,
                bool isFocused)
            {
                if (BodyY(virtualRow) is { } y)
                    DrawInput(contentX, y, contentWidth, buffer, history, isFocused);
            }

            void DrawBodySeparator(int virtualRow)
            {
                if (BodyY(virtualRow) is { } y)
                    DrawSeparator(bounds, y);
            }

            void DrawBodyAccessRights(int virtualRow)
            {
                if (BodyY(virtualRow) is { } y)
                    DrawAccessRights(contentX, y, contentWidth, securityMode, focusRow == 1, fill, focused);
            }

            void DrawBodyCheckbox(int virtualRow, string label, bool value, bool isFocused)
            {
                if (BodyY(virtualRow) is { } y)
                    CheckBoxForLabel(label, value).Render(_screen, contentX, y, contentWidth, isFocused);
            }

            void DrawBodyConflictModeRow(int virtualRow, int startIndex, int endIndex)
            {
                if (BodyY(virtualRow) is { } y)
                    DrawConflictModeRow(contentX, y, contentWidth, conflictModes, conflictIndex, startIndex, endIndex, focusRow == 2, fill, focused);
            }
        });

        Rect frameBounds = new(
            outerBounds.X + 1,
            outerBounds.Y + 1,
            Math.Max(1, outerBounds.Width - 2),
            Math.Max(1, outerBounds.Height - 2));
        int inputX = frameBounds.X + 2;
        int inputWidth = Math.Max(1, frameBounds.Width - 4);
        if (focusRow == 0 && InputCursorY(frameBounds, bodyScrollTop, 1) is { } destinationY)
        {
            SingleLineTextInput.RenderHistoryDropdown(_screen, inputX, destinationY, inputWidth, destinationHistory);
            SetInputCursor(inputX, destinationY, inputWidth, destination, hasHistory: true);
        }
        else if (focusRow == 6 && InputCursorY(frameBounds, bodyScrollTop, 14) is { } filterY)
        {
            SingleLineTextInput.RenderHistoryDropdown(_screen, inputX, filterY, inputWidth, filterHistory);
            SetInputCursor(inputX, filterY, inputWidth, filter, hasHistory: true);
        }
        else
            _screen.SetCursorVisible(false);
    }

    private static int NormalizeBodyScroll(ConsoleSize size, int focusRow, int bodyScrollTop)
    {
        int viewportRows = FileOperationBodyViewportRows(size);
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
        int focusVirtualRow = FocusVirtualRow(focusRow);
        if (focusVirtualRow >= 0)
            bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, bodyScrollTop, viewportRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, BodyRowCount, viewportRows);
    }

    private static int FileOperationBodyViewportRows(ConsoleSize size)
    {
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int frameHeight = Math.Max(1, dialogHeight - 2);
        int buttonRow = frameHeight - 2;
        int errorRow = buttonRow - 1;
        return Math.Max(1, errorRow - 1);
    }

    private static int FocusVirtualRow(int focusRow) => focusRow switch
    {
        0 => 1,
        1 => 3,
        2 => 6,
        3 => 9,
        4 => 10,
        5 => 12,
        6 => 14,
        _ => -1,
    };

    private static int? InputCursorY(Rect frameBounds, int bodyScrollTop, int virtualRow)
    {
        int buttonY = frameBounds.Y + frameBounds.Height - 2;
        int errorY = buttonY - 1;
        int bodyTop = frameBounds.Y + 1;
        int bodyHeight = Math.Max(1, errorY - bodyTop);
        int row = virtualRow - bodyScrollTop;
        return row >= 0 && row < bodyHeight ? bodyTop + row : null;
    }

    private static bool TryHandleHistoryArrow(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int bodyScrollTop,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory,
        out int focusRow)
    {
        focusRow = -1;
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        Rect frameBounds = FrameBounds(size);
        int inputX = frameBounds.X + 2;
        int inputWidth = Math.Max(1, frameBounds.Width - 4);
        if (InputCursorY(frameBounds, bodyScrollTop, 1) is { } destinationY &&
            SingleLineTextInput.IsHistoryArrowHit(inputX, inputWidth, destinationY, mouse.X, mouse.Y))
        {
            focusRow = 0;
            return SingleLineTextInput.TryOpenHistoryDropdown(destinationHistory, destinationY, size.Height);
        }

        if (InputCursorY(frameBounds, bodyScrollTop, 14) is { } filterY &&
            SingleLineTextInput.IsHistoryArrowHit(inputX, inputWidth, filterY, mouse.X, mouse.Y))
        {
            focusRow = 6;
            return SingleLineTextInput.TryOpenHistoryDropdown(filterHistory, filterY, size.Height);
        }

        return false;
    }

    private static bool TryHandleHistoryDropdownMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int bodyScrollTop,
        CommandLineState destination,
        CommandLineState filter,
        SingleLineTextHistoryState destinationHistory,
        SingleLineTextHistoryState filterHistory,
        ref ScrollBarDragState? historyScrollbarDrag)
    {
        Rect frameBounds = FrameBounds(size);
        int inputX = frameBounds.X + 2;
        int inputWidth = Math.Max(1, frameBounds.Width - 4);
        return TryHandleHistoryDropdownRow(mouse, size, bodyScrollTop, frameBounds, inputX, inputWidth, 1, destination, destinationHistory, ref historyScrollbarDrag) ||
               TryHandleHistoryDropdownRow(mouse, size, bodyScrollTop, frameBounds, inputX, inputWidth, 14, filter, filterHistory, ref historyScrollbarDrag);
    }

    private static bool TryHandleHistoryDropdownRow(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int bodyScrollTop,
        Rect frameBounds,
        int inputX,
        int inputWidth,
        int virtualRow,
        CommandLineState buffer,
        SingleLineTextHistoryState history,
        ref ScrollBarDragState? historyScrollbarDrag)
    {
        if (InputCursorY(frameBounds, bodyScrollTop, virtualRow) is not { } fieldY)
            return false;

        return SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            mouse,
            inputX,
            fieldY,
            inputWidth,
            size.Height,
            ref historyScrollbarDrag);
    }

    private static int DropdownRows(ConsoleSize size, int focusRow, int bodyScrollTop)
    {
        Rect frameBounds = FrameBounds(size);
        int? fieldY = focusRow switch
        {
            0 => InputCursorY(frameBounds, bodyScrollTop, 1),
            6 => InputCursorY(frameBounds, bodyScrollTop, 14),
            _ => null,
        };
        return fieldY is null
            ? 0
            : SingleLineTextInput.AvailableDropdownContentRows(fieldY.Value, size.Height);
    }

    private static Rect FrameBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(
            dialogX + 1,
            dialogY + 1,
            Math.Max(1, dialogWidth - 2),
            Math.Max(1, dialogHeight - 2));
    }

    private void DrawInput(
        int x,
        int y,
        int width,
        CommandLineState buffer,
        SingleLineTextHistoryState history,
        bool focused)
    {
        SingleLineTextInput.Render(
            _screen,
            x,
            y,
            width,
            buffer,
            focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            history,
            renderDropdown: false);
    }

    private void SetInputCursor(int x, int y, int width, CommandLineState buffer, bool hasHistory)
    {
        int textWidth = hasHistory ? Math.Max(1, width - 1) : width;
        int cursorX = SingleLineTextInput.GetCursorX(x, textWidth, buffer);
        if (cursorX < x + textWidth)
        {
            _screen.SetCursorPosition(cursorX, y);
            _screen.SetCursorVisible(true);
        }
    }

    private void DrawAccessRights(
        int x,
        int y,
        int width,
        FileSecurityMode mode,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        string text = mode switch
        {
            FileSecurityMode.CopyAccessControl => "Access rights: ( ) Default (x) Copy ( ) Inherit",
            FileSecurityMode.Inherit => "Access rights: ( ) Default ( ) Copy (x) Inherit",
            _ => "Access rights: (x) Default ( ) Copy ( ) Inherit",
        };
        _screen.Write(x, y, Truncate(text, width).PadRight(width), focused ? focusedStyle : fill);
    }

    private void DrawConflictModes(
        int x,
        int y,
        int width,
        IReadOnlyList<ConflictDecisionMode> modes,
        int selectedIndex,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        _screen.Write(x, y, "Already existing files:".PadRight(width), fill);
        DrawConflictModeRow(x, y + 1, width, modes, selectedIndex, 0, Math.Min(4, modes.Count), focused, fill, focusedStyle);
        DrawConflictModeRow(x, y + 2, width, modes, selectedIndex, 4, modes.Count, focused, fill, focusedStyle);
    }

    private void DrawConflictModeRow(
        int x,
        int y,
        int width,
        IReadOnlyList<ConflictDecisionMode> modes,
        int selectedIndex,
        int startIndex,
        int endIndex,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        var labels = new List<string>();
        for (int i = startIndex; i < endIndex; i++)
            labels.Add($"{(i == selectedIndex ? "(x)" : "( )")} {ConflictLabel(modes[i])}");

        string text = string.Join(' ', labels);
        _screen.Write(x, y, Truncate(text, width).PadRight(width), focused ? focusedStyle : fill);
    }

    private CheckBoxLine CheckBoxForLabel(string label, bool value)
    {
        CheckBoxLine checkBox = label switch
        {
            "Preserve all timestamps" => _preserveTimestamps,
            "Copy contents of symbolic links" => _copySymlinkContents,
            "Use filter" => _useFilter,
            _ => throw new InvalidOperationException($"Unknown checkbox label: {label}"),
        };
        checkBox.Value = value;
        return checkBox;
    }

    private void DrawSeparator(Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        var style = FarDialogStyles.Border;
        _screen.WriteChar(bounds.X, y, '╟', style);
        _screen.Write(bounds.X + 1, y, new string('─', Math.Max(0, bounds.Width - 2)), style);
        _screen.WriteChar(bounds.Right - 1, y, '╢', style);
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

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }

}
