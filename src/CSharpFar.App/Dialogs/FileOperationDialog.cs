using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

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
    ];

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
        string title = "Copy";
        string prompt = sources.Count == 1
            ? $"Copy {Path.GetFileName(sources[0])} to:"
            : $"Copy {sources.Count} items to:";
        return Show(title, prompt, "Copy", initialDestination, initialOptions, CopyConflictModes);
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
        return Show(title, prompt, "Move", initialDestination, initialOptions, MoveConflictModes);
    }

    private FileOperationDialogResult? Show(
        string title,
        string prompt,
        string actionLabel,
        string initialDestination,
        FileOperationOptions initialOptions,
        IReadOnlyList<ConflictDecisionMode> conflictModes)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, title, prompt, actionLabel, initialDestination, initialOptions, conflictModes);
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
        IReadOnlyList<ConflictDecisionMode> conflictModes)
    {
        var destination = new CommandLineState();
        destination.SetText(initialDestination);

        var filter = new CommandLineState();
        if (!string.IsNullOrWhiteSpace(initialOptions.FileMask))
            filter.SetText(initialOptions.FileMask);
        else
            filter.SetText("*");

        int conflictIndex = FindConflictIndex(initialOptions.DefaultConflictDecision, conflictModes);
        var securityMode = initialOptions.SecurityMode;
        bool preserveTimestamps = initialOptions.PreserveTimestamps;
        bool copySymlinkContents = initialOptions.SymlinkMode == SymlinkCopyMode.CopyTargetContents;
        bool useFilter = !string.IsNullOrWhiteSpace(initialOptions.FileMask);
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("submit", actionLabel, actionLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

        Draw(
            size,
            title,
            prompt,
            actionLabel,
            destination,
            filter,
            conflictModes,
            conflictIndex,
            securityMode,
            preserveTimestamps,
            copySymlinkContents,
            useFilter,
            focusRow,
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
                        securityMode, preserveTimestamps, copySymlinkContents, useFilter, ref error);
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
                    conflictModes,
                    conflictIndex,
                    securityMode,
                    preserveTimestamps,
                    copySymlinkContents,
                    useFilter,
                    focusRow,
                    buttonBar,
                    focusedButton,
                    error);
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
                    securityMode, preserveTimestamps, copySymlinkContents, useFilter, ref error);
                if (result is not null)
                    return result;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                Draw(
                    size,
                    title,
                    prompt,
                    actionLabel,
                    destination,
                    filter,
                    conflictModes,
                    conflictIndex,
                    securityMode,
                    preserveTimestamps,
                    copySymlinkContents,
                    useFilter,
                    focusRow,
                    buttonBar,
                    focusedButton,
                    error);
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    var f10Result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                        securityMode, preserveTimestamps, copySymlinkContents, useFilter, ref error);
                    if (f10Result is not null)
                        return f10Result;
                    break;
                case ConsoleKey.Enter:
                    if (focusRow is 0 or 6 or 7)
                    {
                        if (focusRow == 7 && focusedButton == 1)
                            return null;

                        var result = BuildResult(destination, filter, initialOptions, conflictModes[conflictIndex],
                            securityMode, preserveTimestamps, copySymlinkContents, useFilter, ref error);
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
                            securityMode, preserveTimestamps, copySymlinkContents, useFilter, ref error);
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
                        EditText(focusRow == 0 ? destination : filter, key, ref error);
                    }
                    break;
                case ConsoleKey.LeftArrow:
                    if (focusRow == 7)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow == 2)
                        conflictIndex = conflictIndex <= 0 ? conflictModes.Count - 1 : conflictIndex - 1;
                    else if (focusRow is 0 or 6)
                        EditText(focusRow == 0 ? destination : filter, key, ref error);
                    break;
                case ConsoleKey.RightArrow:
                    if (focusRow == 7)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else if (focusRow == 2)
                        conflictIndex = (conflictIndex + 1) % conflictModes.Count;
                    else if (focusRow is 0 or 6)
                        EditText(focusRow == 0 ? destination : filter, key, ref error);
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = PreviousFocusableRow(focusRow, useFilter);
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                    focusRow = NextFocusableRow(focusRow, useFilter);
                    break;
                default:
                    if (focusRow is 0 or 6)
                        EditText(focusRow == 0 ? destination : filter, key, ref error);
                    break;
            }

            if (!useFilter && focusRow == 6)
                focusRow = 5;

            Draw(
                size,
                title,
                prompt,
                actionLabel,
                destination,
                filter,
                conflictModes,
                conflictIndex,
                securityMode,
                preserveTimestamps,
                copySymlinkContents,
                useFilter,
                focusRow,
                buttonBar,
                focusedButton,
                error);
        }
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

    private static void EditText(CommandLineState buffer, ConsoleKeyInfo key, ref string? error)
    {
        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

        if (isPrintable)
        {
            buffer.Insert(key.KeyChar);
            error = null;
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.Backspace: buffer.DeleteBack(); error = null; break;
            case ConsoleKey.Delete: buffer.DeleteForward(); error = null; break;
            case ConsoleKey.LeftArrow: buffer.MoveCursor(-1); break;
            case ConsoleKey.RightArrow: buffer.MoveCursor(+1); break;
            case ConsoleKey.Home: buffer.MoveToStart(); break;
            case ConsoleKey.End: buffer.MoveToEnd(); break;
        }
    }

    private static void CycleFocusedValue(
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
                preserveTimestamps = !preserveTimestamps;
                break;
            case 4:
                copySymlinkContents = !copySymlinkContents;
                break;
            case 5:
                useFilter = !useFilter;
                break;
        }
    }

    private static int NextFocusableRow(int focusRow, bool useFilter)
    {
        if (focusRow == 5 && useFilter)
            return 6;
        if (focusRow == 5 || focusRow == 6)
            return 7;
        if (focusRow >= 7)
            return 0;
        return focusRow + 1;
    }

    private static int PreviousFocusableRow(int focusRow, bool useFilter)
    {
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

    private void Draw(
        ConsoleSize size,
        string title,
        string prompt,
        string actionLabel,
        CommandLineState destination,
        CommandLineState filter,
        IReadOnlyList<ConflictDecisionMode> conflictModes,
        int conflictIndex,
        FileSecurityMode securityMode,
        bool preserveTimestamps,
        bool copySymlinkContents,
        bool useFilter,
        int focusRow,
        DialogButtonBar buttonBar,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(12, size.Height - 2));
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

            _screen.Write(contentX, bounds.Y + 1, Truncate(prompt, contentWidth).PadRight(contentWidth), fill);
            DrawInput(contentX, bounds.Y + 2, contentWidth, destination, focusRow == 0);

            DrawSeparator(bounds, bounds.Y + 3);
            DrawAccessRights(contentX, bounds.Y + 4, contentWidth, securityMode, focusRow == 1, fill, focused);

            DrawSeparator(bounds, bounds.Y + 5);
            DrawConflictModes(contentX, bounds.Y + 6, contentWidth, conflictModes, conflictIndex, focusRow == 2, fill, focused);
            DrawSeparator(bounds, bounds.Y + 9);
            DrawCheckbox(contentX, bounds.Y + 10, contentWidth, "Preserve all timestamps", preserveTimestamps, focusRow == 3, fill, focused);
            DrawCheckbox(contentX, bounds.Y + 11, contentWidth, "Copy contents of symbolic links", copySymlinkContents, focusRow == 4, fill, focused);
            DrawSeparator(bounds, bounds.Y + 12);
            DrawCheckbox(contentX, bounds.Y + 13, contentWidth, "Use filter", useFilter, focusRow == 5, fill, focused);
            _screen.Write(contentX, bounds.Y + 14, "Filter mask:".PadRight(contentWidth), fill);
            if (useFilter)
                DrawInput(contentX, bounds.Y + 15, contentWidth, filter, focusRow == 6);
            else
                _screen.Write(contentX, bounds.Y + 15, VisibleInputText(filter, contentWidth).PadRight(contentWidth), fill);

            DrawSeparator(bounds, bounds.Y + 16);
            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, bounds.Y + 17, errorText.PadRight(contentWidth), FarDialogStyles.Error);

            buttonBar.Render(
                _screen,
                contentX,
                bounds.Y + bounds.Height - 2,
                contentWidth,
                focusedButton,
                fill,
                focusRow == 7 ? focused : fill);
        });

        if (focusRow == 0)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 3, Math.Max(1, outerBounds.Width - 6), destination);
        else if (focusRow == 6)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 16, Math.Max(1, outerBounds.Width - 6), filter);
        else
            _screen.SetCursorVisible(false);
    }

    private void DrawInput(int x, int y, int width, CommandLineState buffer, bool focused)
    {
        string text = VisibleInputText(buffer, width);
        _screen.Write(x, y, text.PadRight(width), focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
    }

    private void SetInputCursor(int x, int y, int width, CommandLineState buffer)
    {
        int start = Math.Max(0, buffer.CursorPosition - (width - 1));
        int cursorX = x + buffer.CursorPosition - start;
        if (cursorX < x + width)
        {
            _screen.SetCursorPosition(cursorX, y);
            _screen.SetCursorVisible(true);
        }
    }

    private static string VisibleInputText(CommandLineState buffer, int width)
    {
        int start = Math.Max(0, buffer.CursorPosition - (width - 1));
        string visible = buffer.Text.Length > start ? buffer.Text[start..] : string.Empty;
        return visible.Length > width ? visible[..width] : visible;
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

    private void DrawCheckbox(
        int x,
        int y,
        int width,
        string label,
        bool value,
        bool focused,
        CellStyle fill,
        CellStyle focusedStyle)
    {
        string text = $"[{(value ? "x" : " ")}] {label}";
        _screen.Write(x, y, Truncate(text, width).PadRight(width), focused ? focusedStyle : fill);
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
