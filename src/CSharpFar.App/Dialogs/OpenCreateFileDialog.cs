using CSharpFar.App.Editor;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record OpenCreateFileDialogResult(
    string FilePath,
    EditorNewFileEncodingOption CodePage);

internal sealed class OpenCreateFileDialog
{
    private const int DialogWidth = 72;
    private const int DialogHeight = 11;
    private const string Title = "Editor";

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();

    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly IReadOnlyList<EditorNewFileEncodingOption> _codePages;
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public OpenCreateFileDialog(ModalDialogHost modalDialogs)
        : this(modalDialogs, EditorNewFileEncodingOption.CreateCatalog())
    {
    }

    internal OpenCreateFileDialog(
        ModalDialogHost modalDialogs,
        IReadOnlyList<EditorNewFileEncodingOption> codePages)
    {
        _modalDialogs = modalDialogs;
        _screen = modalDialogs.Screen;
        _codePages = codePages.Count == 0
            ? [new EditorNewFileEncodingOption("Default", null, EmitByteOrderMark: false)]
            : codePages;
    }

    public OpenCreateFileDialogResult? Show(
        string? initialPath = null,
        Func<string, string?>? validate = null)
    {
        _screen.SetCursorVisible(false);

        return RunLoop(initialPath, validate);
    }

    private OpenCreateFileDialogResult? RunLoop(
        string? initialPath,
        Func<string, string?>? validate)
    {
        var filePath = new CommandLineState();
        if (!string.IsNullOrEmpty(initialPath))
            filePath.SetText(initialPath);

        SingleLineTextHistoryState history = HistoryRegistry.GetOrCreate("OpenCreateFileDialog.FilePath");
        var codePageDropdown = new DropdownSelect<EditorNewFileEncodingOption>(_codePages, static item => item.Label);
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;
        ScrollBarDragState? historyScrollbarDrag = null;
        return _modalDialogs.Run(
            context => Draw(context.Screen, context.Size, filePath, history, codePageDropdown, focusRow, focusedButton, error),
            (input, frame) =>
            {

            if (focusRow == 2 &&
                _buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);
                if (buttonId == "ok")
                {
                    var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                    if (result is not null)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(result);
                }

                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            }

            if (input is MouseConsoleInputEvent dropdownMouse &&
                frame.History is { } historyFrame &&
                TryHandleHistoryDropdownMouse(dropdownMouse, filePath, history, historyFrame, ref historyScrollbarDrag))
            {
                focusRow = 0;
                codePageDropdown.Close();
                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                if (codePageDropdown.TryHandlePopupMouse(mouse, frame.CodePageDropdown, out _))
                {
                    focusRow = 1;
                    error = null;
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }

                if (TryHandleHistoryArrow(mouse, frame, history))
                {
                    focusRow = 0;
                    codePageDropdown.Close();
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }

                if (TryHandlePathFieldMouse(mouse, frame.PathFieldBounds, filePath))
                {
                    focusRow = 0;
                    codePageDropdown.Close();
                    error = null;
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }

                if (codePageDropdown.TryHandleFieldMouse(mouse, frame.CodePageDropdown))
                {
                    focusRow = 1;
                    error = null;
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }

                if (_buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out buttonId) &&
                    buttonId is not null)
                {
                    focusRow = 2;
                    codePageDropdown.Close();
                    if (buttonId == "cancel")
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                        var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                    if (result is not null)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(result);

                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }

                if (codePageDropdown.IsOpen)
                {
                    codePageDropdown.Close();
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
                }
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;

            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(frame.PathFieldBounds.Y, frame.Size.Height);
            if (focusRow == 0 &&
                history.IsDropdownOpen &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(filePath, key, ref error, history, availableRows);
                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            }

            if (focusRow == 1 && codePageDropdown.IsOpen)
            {
                if (key.Key == ConsoleKey.Tab)
                {
                    codePageDropdown.Close();
                    focusRow = 2;
                }
                else
                {
                    codePageDropdown.TryHandleKey(key, frame.CodePageDropdown, out _);
                }

                return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                case ConsoleKey.F10:
                case ConsoleKey.Enter:
                    if (focusRow == 2 && focusedButton == 1)
                        return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                    {
                    var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                        if (result is not null)
                            return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(result);
                        break;
                    }

                case ConsoleKey.Tab:
                    codePageDropdown.Close();
                    focusRow = (focusRow + 1) % 3;
                    break;

                case ConsoleKey.UpArrow:
                    codePageDropdown.Close();
                    focusRow = Math.Max(0, focusRow - 1);
                    break;

                case ConsoleKey.DownArrow:
                    if (focusRow == 1)
                    {
                        codePageDropdown.Open();
                    }
                    else
                    {
                        codePageDropdown.Close();
                        focusRow = Math.Min(2, focusRow + 1);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (focusRow == 1)
                        codePageDropdown.Open();
                    else if (focusRow == 2)
                        _buttonBar.TryHandleKey(key, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.RightArrow:
                    if (focusRow == 1)
                        codePageDropdown.Open();
                    else if (focusRow == 2)
                        _buttonBar.TryHandleKey(key, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                    {
                        codePageDropdown.Open();
                    }
                    else if (focusRow == 2)
                    {
                        if (focusedButton == 1)
                            return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(null);

                        var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                        if (result is not null)
                            return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Complete(result);
                    }
                    else
                    {
                        EditText(filePath, key, history, availableRows, ref error);
                    }
                    break;

                default:
                    if (focusRow == 0)
                        EditText(filePath, key, history, availableRows, ref error);
                    break;
            }

            return ModalDialogLoopResult<OpenCreateFileDialogResult?>.Continue;
            },
            applyCommittedFrame: frame => codePageDropdown.ApplyCommittedFrame(frame.CodePageDropdown));
    }

    private OpenCreateFileDialogResult? TrySubmit(
        CommandLineState filePath,
        SingleLineTextHistoryState history,
        int codePageIndex,
        Func<string, string?>? validate,
        ref string? error)
    {
        string path = filePath.Text.Trim();
        if (path.Length == 0)
        {
            error = "File path is required.";
            return null;
        }

        error = validate?.Invoke(path);
        if (error is not null)
            return null;

        history.Add(path);
        return new OpenCreateFileDialogResult(path, _codePages[codePageIndex]);
    }

    private static void EditText(
        CommandLineState buffer,
        ConsoleKeyInfo key,
        SingleLineTextHistoryState history,
        int availableRows,
        ref string? error)
    {
        SingleLineTextInput.HandleKey(buffer, key, ref error, history, availableRows);
    }

    private OpenCreateFileFrame Draw(
        ScreenRenderer screen,
        ConsoleSize size,
        CommandLineState filePath,
        SingleLineTextHistoryState history,
        DropdownSelect<EditorNewFileEncodingOption> codePageDropdown,
        int focusRow,
        int focusedButton,
        string? error)
    {
        DialogButtonBarLayout buttons = null!;
        int dialogWidth = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        _modalRenderer.Render(
            screen,
            outerBounds,
            Title,
            doubleBorder: true,
            FarDialogStyles.OuterOptions,
            FarDialogStyles.FrameOptions,
            (_, layout) =>
            {
                Rect bounds = layout.ContentBounds;
                int contentX = bounds.X + 1;
                int contentWidth = Math.Max(1, bounds.Width - 2);

                screen.Write(contentX, bounds.Y, "Open/create file:".PadRight(contentWidth), FarDialogStyles.Fill);
                DrawPathInput(contentX, bounds.Y + 1, contentWidth, filePath, history, focusRow == 0);

                screen.Write(contentX, bounds.Y + 3, "Code page:".PadRight(contentWidth), FarDialogStyles.Fill);
                codePageDropdown.RenderField(
                    screen,
                    new Rect(contentX, bounds.Y + 4, contentWidth, 1),
                    focusRow == 1 ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);

                string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
                screen.Write(contentX, bounds.Y + 5, errorText.PadRight(contentWidth), FarDialogStyles.Error);

                buttons = _buttonBar.Render(
                    screen,
                    contentX,
                    layout.FrameBounds.Bottom - 2,
                    contentWidth,
                    focusedButton,
                    FarDialogStyles.Fill,
                    focusRow == 2 ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
            });

        var pathFieldBounds = PathFieldBounds(outerBounds);
        var codePageFieldBounds = CodePageFieldBounds(outerBounds);
        var historyFrame = SingleLineTextInput.CalculateHistoryDropdownFrame(
            pathFieldBounds.X,
            pathFieldBounds.Y,
            pathFieldBounds.Width,
            size.Height,
            history);
        var dropdownFrame = codePageDropdown.CalculateFrame(size, codePageFieldBounds);

        if (focusRow == 0)
        {
            if (historyFrame is { } value)
                SingleLineTextInput.RenderHistoryDropdown(screen, history, value);
        }
        else
        {
            codePageDropdown.RenderPopup(screen, dropdownFrame);
        }

        if (focusRow == 0)
            SetInputCursor(pathFieldBounds.X, pathFieldBounds.Y, pathFieldBounds.Width, filePath);
        else
            screen.SetCursorVisible(false);
        return new OpenCreateFileFrame(size, pathFieldBounds, dropdownFrame, historyFrame, buttons);
    }

    private void DrawPathInput(
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

    private void SetInputCursor(int x, int y, int width, CommandLineState buffer)
    {
        int textWidth = Math.Max(1, width - 1);
        int cursorX = SingleLineTextInput.GetCursorX(x, textWidth, buffer);
        if (cursorX < x + textWidth)
        {
            _screen.SetCursorPosition(cursorX, y);
            _screen.SetCursorVisible(true);
        }
    }

    private static bool TryHandlePathFieldMouse(
        MouseConsoleInputEvent mouse,
        Rect bounds,
        CommandLineState filePath)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click) ||
            mouse.Y != bounds.Y ||
            mouse.X < bounds.X ||
            mouse.X >= bounds.Right)
        {
            return false;
        }

        int textWidth = Math.Max(1, bounds.Width - 1);
        int visibleStart = Math.Max(0, filePath.CursorPosition - Math.Max(0, textWidth - 1));
        int clickedPosition = visibleStart + Math.Min(mouse.X - bounds.X, textWidth - 1);
        filePath.MoveCursorTo(clickedPosition);
        return true;
    }

    private static bool TryHandleHistoryArrow(
        MouseConsoleInputEvent mouse,
        OpenCreateFileFrame frame,
        SingleLineTextHistoryState history)
    {
        int fieldX = frame.PathFieldBounds.X;
        int fieldY = frame.PathFieldBounds.Y;
        int fieldWidth = frame.PathFieldBounds.Width;
        if (!SingleLineTextInput.IsHistoryArrowHit(fieldX, fieldWidth, fieldY, mouse.X, mouse.Y))
            return false;

        return SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY, frame.Size.Height);
    }

    private static bool TryHandleHistoryDropdownMouse(
        MouseConsoleInputEvent mouse,
        CommandLineState filePath,
        SingleLineTextHistoryState history,
        SingleLineTextHistoryFrame frame,
        ref ScrollBarDragState? historyScrollbarDrag) =>
        SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            filePath,
            mouse,
            frame,
            ref historyScrollbarDrag);

    private static Rect PathFieldBounds(Rect outerBounds) =>
        new(outerBounds.X + 3, outerBounds.Y + 3, Math.Max(1, outerBounds.Width - 6), 1);

    private static Rect CodePageFieldBounds(Rect outerBounds)
    {
        var path = PathFieldBounds(outerBounds);
        return new Rect(path.X, path.Y + 3, path.Width, 1);
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }

    private readonly record struct OpenCreateFileFrame(
        ConsoleSize Size,
        Rect PathFieldBounds,
        DropdownSelectFrame CodePageDropdown,
        SingleLineTextHistoryFrame? History,
        DialogButtonBarLayout Buttons);
}
