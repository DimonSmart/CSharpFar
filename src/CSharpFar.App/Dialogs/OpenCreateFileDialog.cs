using CSharpFar.App.Editor;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

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

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly IReadOnlyList<EditorNewFileEncodingOption> _codePages;
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public OpenCreateFileDialog(ScreenRenderer screen)
        : this(screen, EditorNewFileEncodingOption.CreateCatalog())
    {
    }

    internal OpenCreateFileDialog(
        ScreenRenderer screen,
        IReadOnlyList<EditorNewFileEncodingOption> codePages)
    {
        _screen = screen;
        _codePages = codePages.Count == 0
            ? [new EditorNewFileEncodingOption("Default", null, EmitByteOrderMark: false)]
            : codePages;
    }

    public OpenCreateFileDialogResult? Show(
        string? initialPath = null,
        Func<string, string?>? validate = null)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, initialPath, validate);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private OpenCreateFileDialogResult? RunLoop(
        ConsoleSize size,
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

        Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);

        while (true)
        {
            var input = _screen.ReadInput();

            if (focusRow == 2 &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "ok")
                {
                    var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                    if (result is not null)
                        return result;
                }

                Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                continue;
            }

            if (input is MouseConsoleInputEvent dropdownMouse &&
                TryHandleHistoryDropdownMouse(dropdownMouse, size, filePath, history, ref historyScrollbarDrag))
            {
                focusRow = 0;
                codePageDropdown.Close(_screen);
                Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                if (codePageDropdown.TryHandlePopupMouse(mouse, _screen, size, CodePageFieldBounds(size), out _))
                {
                    focusRow = 1;
                    error = null;
                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }

                if (TryHandleHistoryArrow(mouse, size, history))
                {
                    focusRow = 0;
                    codePageDropdown.Close(_screen);
                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }

                if (TryHandlePathFieldMouse(mouse, size, filePath))
                {
                    focusRow = 0;
                    codePageDropdown.Close(_screen);
                    error = null;
                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }

                if (codePageDropdown.TryHandleFieldMouse(mouse, size, CodePageFieldBounds(size)))
                {
                    focusRow = 1;
                    error = null;
                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }

                if (_buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                    buttonId is not null)
                {
                    focusRow = 2;
                    codePageDropdown.Close(_screen);
                    if (buttonId == "cancel")
                        return null;

                        var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                    if (result is not null)
                        return result;

                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }

                if (codePageDropdown.IsOpen)
                {
                    codePageDropdown.Close(_screen);
                    Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                    continue;
                }
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                continue;
            }

            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(InputY(size), size.Height);
            if (focusRow == 0 &&
                history.IsDropdownOpen &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(filePath, key, ref error, history, availableRows);
                Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                continue;
            }

            if (focusRow == 1 && codePageDropdown.IsOpen)
            {
                if (key.Key == ConsoleKey.Tab)
                {
                    codePageDropdown.Close(_screen);
                    focusRow = 2;
                }
                else
                {
                    codePageDropdown.TryHandleKey(key, size, CodePageFieldBounds(size), _screen, out _);
                }

                Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;

                case ConsoleKey.F10:
                case ConsoleKey.Enter:
                    if (focusRow == 2 && focusedButton == 1)
                        return null;

                    {
                    var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                        if (result is not null)
                            return result;
                        break;
                    }

                case ConsoleKey.Tab:
                    codePageDropdown.Close(_screen);
                    focusRow = (focusRow + 1) % 3;
                    break;

                case ConsoleKey.UpArrow:
                    codePageDropdown.Close(_screen);
                    focusRow = Math.Max(0, focusRow - 1);
                    break;

                case ConsoleKey.DownArrow:
                    if (focusRow == 1)
                    {
                        codePageDropdown.Open(size, CodePageFieldBounds(size));
                    }
                    else
                    {
                        codePageDropdown.Close(_screen);
                        focusRow = Math.Min(2, focusRow + 1);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (focusRow == 1)
                        codePageDropdown.Open(size, CodePageFieldBounds(size));
                    else if (focusRow == 2)
                        _buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.RightArrow:
                    if (focusRow == 1)
                        codePageDropdown.Open(size, CodePageFieldBounds(size));
                    else if (focusRow == 2)
                        _buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                    {
                        codePageDropdown.Open(size, CodePageFieldBounds(size));
                    }
                    else if (focusRow == 2)
                    {
                        if (focusedButton == 1)
                            return null;

                        var result = TrySubmit(filePath, history, codePageDropdown.SelectedIndex, validate, ref error);
                        if (result is not null)
                            return result;
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

            Draw(size, filePath, history, codePageDropdown, focusRow, focusedButton, error);
        }
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

    private void Draw(
        ConsoleSize size,
        CommandLineState filePath,
        SingleLineTextHistoryState history,
        DropdownSelect<EditorNewFileEncodingOption> codePageDropdown,
        int focusRow,
        int focusedButton,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int dialogWidth = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        _modalRenderer.Render(
            _screen,
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

                _screen.Write(contentX, bounds.Y, "Open/create file:".PadRight(contentWidth), FarDialogStyles.Fill);
                DrawPathInput(contentX, bounds.Y + 1, contentWidth, filePath, history, focusRow == 0);

                _screen.Write(contentX, bounds.Y + 3, "Code page:".PadRight(contentWidth), FarDialogStyles.Fill);
                codePageDropdown.RenderField(
                    _screen,
                    new Rect(contentX, bounds.Y + 4, contentWidth, 1),
                    focusRow == 1 ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);

                string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
                _screen.Write(contentX, bounds.Y + 5, errorText.PadRight(contentWidth), FarDialogStyles.Error);

                _buttonBar.Render(
                    _screen,
                    contentX,
                    layout.FrameBounds.Bottom - 2,
                    contentWidth,
                    focusedButton,
                    FarDialogStyles.Fill,
                    focusRow == 2 ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
            });

        if (focusRow == 0)
            SingleLineTextInput.RenderHistoryDropdown(_screen, InputX(size), InputY(size), InputWidth(size), history);
        else
            codePageDropdown.RenderPopup(
                _screen,
                size,
                CodePageFieldBounds(size),
                FarDialogStyles.Input,
                FarDialogStyles.FocusedInput,
                FarDialogStyles.FrameOptions);

        if (focusRow == 0)
            SetInputCursor(InputX(size), InputY(size), InputWidth(size), filePath);
        else
            _screen.SetCursorVisible(false);
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
        ConsoleSize size,
        CommandLineState filePath)
    {
        Rect bounds = PathFieldBounds(size);
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
        ConsoleSize size,
        SingleLineTextHistoryState history)
    {
        int fieldX = InputX(size);
        int fieldY = InputY(size);
        int fieldWidth = InputWidth(size);
        if (!SingleLineTextInput.IsHistoryArrowHit(fieldX, fieldWidth, fieldY, mouse.X, mouse.Y))
            return false;

        return SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY, size.Height);
    }

    private static bool TryHandleHistoryDropdownMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        CommandLineState filePath,
        SingleLineTextHistoryState history,
        ref ScrollBarDragState? historyScrollbarDrag) =>
        SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            filePath,
            mouse,
            InputX(size),
            InputY(size),
            InputWidth(size),
            size.Height,
            ref historyScrollbarDrag);

    private static int InputX(ConsoleSize size) =>
        OuterBounds(size).X + 3;

    private static int InputY(ConsoleSize size) =>
        OuterBounds(size).Y + 3;

    private static int InputWidth(ConsoleSize size) =>
        Math.Max(1, OuterBounds(size).Width - 6);

    private static Rect PathFieldBounds(ConsoleSize size) =>
        new(InputX(size), InputY(size), InputWidth(size), 1);

    private static Rect CodePageFieldBounds(ConsoleSize size) =>
        new(InputX(size), InputY(size) + 3, InputWidth(size), 1);

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
}
