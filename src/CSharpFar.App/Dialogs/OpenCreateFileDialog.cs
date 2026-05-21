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
        int codePageIndex = 0;
        int codePageScrollTop = 0;
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;
        bool codePageDropdownOpen = false;
        ScreenSnapshot? codePageDropdownUnderlay = null;
        ScrollBarDragState? historyScrollbarDrag = null;

        Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);

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
                    var result = TrySubmit(filePath, history, codePageIndex, validate, ref error);
                    if (result is not null)
                        return result;
                }

                Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                continue;
            }

            if (input is MouseConsoleInputEvent dropdownMouse &&
                TryHandleHistoryDropdownMouse(dropdownMouse, size, filePath, history, ref historyScrollbarDrag))
            {
                focusRow = 0;
                codePageDropdownOpen = false;
                Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                if (codePageDropdownOpen &&
                    TryHandleCodePageDropdownMouse(mouse, size, ref codePageIndex, ref codePageScrollTop, ref codePageDropdownOpen))
                {
                    focusRow = 1;
                    error = null;
                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }

                if (TryHandleHistoryArrow(mouse, size, history))
                {
                    focusRow = 0;
                    codePageDropdownOpen = false;
                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }

                if (TryHandlePathFieldMouse(mouse, size, filePath))
                {
                    focusRow = 0;
                    codePageDropdownOpen = false;
                    error = null;
                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }

                if (TryHandleCodePageMouse(mouse, size, codePageIndex, ref codePageScrollTop, ref codePageDropdownOpen))
                {
                    focusRow = 1;
                    error = null;
                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }

                if (_buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                    buttonId is not null)
                {
                    focusRow = 2;
                    codePageDropdownOpen = false;
                    if (buttonId == "cancel")
                        return null;

                    var result = TrySubmit(filePath, history, codePageIndex, validate, ref error);
                    if (result is not null)
                        return result;

                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }

                if (codePageDropdownOpen)
                {
                    codePageDropdownOpen = false;
                    Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                    continue;
                }
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                continue;
            }

            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(InputY(size), size.Height);
            if (focusRow == 0 &&
                history.IsDropdownOpen &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(filePath, key, ref error, history, availableRows);
                Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
                continue;
            }

            if (focusRow == 1 && codePageDropdownOpen)
            {
                switch (key.Key)
                {
                    case ConsoleKey.Escape:
                        codePageDropdownOpen = false;
                        break;
                    case ConsoleKey.Enter:
                    case ConsoleKey.Spacebar:
                        codePageDropdownOpen = false;
                        break;
                    case ConsoleKey.UpArrow:
                        codePageIndex = Math.Max(0, codePageIndex - 1);
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.DownArrow:
                        codePageIndex = Math.Min(_codePages.Count - 1, codePageIndex + 1);
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.PageUp:
                        codePageIndex = Math.Max(0, codePageIndex - CodePageDropdownContentRows(size));
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.PageDown:
                        codePageIndex = Math.Min(_codePages.Count - 1, codePageIndex + CodePageDropdownContentRows(size));
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.Home:
                        codePageIndex = 0;
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.End:
                        codePageIndex = _codePages.Count - 1;
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                        break;
                    case ConsoleKey.Tab:
                        codePageDropdownOpen = false;
                        focusRow = 2;
                        break;
                }

                Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
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
                        var result = TrySubmit(filePath, history, codePageIndex, validate, ref error);
                        if (result is not null)
                            return result;
                        break;
                    }

                case ConsoleKey.Tab:
                    codePageDropdownOpen = false;
                    focusRow = (focusRow + 1) % 3;
                    break;

                case ConsoleKey.UpArrow:
                    codePageDropdownOpen = false;
                    focusRow = Math.Max(0, focusRow - 1);
                    break;

                case ConsoleKey.DownArrow:
                    if (focusRow == 1)
                    {
                        codePageDropdownOpen = true;
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                    }
                    else
                    {
                        codePageDropdownOpen = false;
                        focusRow = Math.Min(2, focusRow + 1);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (focusRow == 1)
                        codePageDropdownOpen = true;
                    else if (focusRow == 2)
                        _buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.RightArrow:
                    if (focusRow == 1)
                        codePageDropdownOpen = true;
                    else if (focusRow == 2)
                        _buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        EditText(filePath, key, history, availableRows, ref error);
                    break;

                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                    {
                        codePageDropdownOpen = true;
                        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
                    }
                    else if (focusRow == 2)
                    {
                        if (focusedButton == 1)
                            return null;

                        var result = TrySubmit(filePath, history, codePageIndex, validate, ref error);
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

            Draw(size, filePath, history, codePageIndex, codePageScrollTop, focusRow, focusedButton, error, codePageDropdownOpen, ref codePageDropdownUnderlay);
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
        int codePageIndex,
        int codePageScrollTop,
        int focusRow,
        int focusedButton,
        string? error,
        bool codePageDropdownOpen,
        ref ScreenSnapshot? codePageDropdownUnderlay)
    {
        if (!codePageDropdownOpen && codePageDropdownUnderlay is not null)
        {
            _screen.Restore(codePageDropdownUnderlay);
            codePageDropdownUnderlay = null;
        }

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
                DrawCodePage(contentX, bounds.Y + 4, contentWidth, _codePages[codePageIndex], focusRow == 1);

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
        else if (codePageDropdownOpen)
            DrawCodePageDropdown(size, codePageIndex, codePageScrollTop, ref codePageDropdownUnderlay);

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

    private void DrawCodePage(
        int x,
        int y,
        int width,
        EditorNewFileEncodingOption option,
        bool focused)
    {
        string text = width > 1
            ? Fit(option.Label, width - 1) + "\u2193"
            : "\u2193";
        _screen.Write(
            x,
            y,
            text,
            focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
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

    private bool TryHandleCodePageMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int codePageIndex,
        ref int codePageScrollTop,
        ref bool codePageDropdownOpen)
    {
        var bounds = CodePageFieldBounds(size);
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Down ||
            mouse.Y != bounds.Y ||
            mouse.X < bounds.X ||
            mouse.X >= bounds.Right)
        {
            return false;
        }

        EnsureCodePageVisible(codePageIndex, ref codePageScrollTop, CodePageDropdownContentRows(size));
        codePageDropdownOpen = !codePageDropdownOpen;
        return true;
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

    private bool TryHandleCodePageDropdownMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        ref int codePageIndex,
        ref int codePageScrollTop,
        ref bool codePageDropdownOpen)
    {
        if (mouse.Kind != MouseEventKind.Down && mouse.Kind != MouseEventKind.Wheel)
            return false;

        Rect dropdownBounds = CodePageDropdownBounds(size);
        int contentRows = CodePageDropdownContentRows(size);
        if (mouse.Kind == MouseEventKind.Wheel)
        {
            if (mouse.Button == MouseButton.WheelUp)
                codePageScrollTop = Math.Max(0, codePageScrollTop - 1);
            else if (mouse.Button == MouseButton.WheelDown)
                codePageScrollTop = Math.Min(MaxCodePageScrollTop(contentRows), codePageScrollTop + 1);
            else
                return false;

            return true;
        }

        if (mouse.Button != MouseButton.Left)
            return false;

        if (mouse.X < dropdownBounds.X ||
            mouse.X >= dropdownBounds.Right ||
            mouse.Y < dropdownBounds.Y ||
            mouse.Y >= dropdownBounds.Bottom)
        {
            codePageDropdownOpen = false;
            return true;
        }

        int itemRow = mouse.Y - dropdownBounds.Y - 1;
        if (itemRow < 0 || itemRow >= contentRows)
            return true;

        int itemIndex = codePageScrollTop + itemRow;
        if (itemIndex >= _codePages.Count)
            return true;

        codePageIndex = itemIndex;
        codePageDropdownOpen = false;
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

    private static Rect CodePageDropdownBounds(ConsoleSize size)
    {
        Rect field = CodePageFieldBounds(size);
        int contentRows = CodePageDropdownContentRows(size);
        int height = contentRows + 2;
        int y = field.Y + 1;
        if (y + height > size.Height)
            y = Math.Max(0, field.Y - height);

        return new Rect(field.X, y, field.Width, height);
    }

    private static int CodePageDropdownContentRows(ConsoleSize size)
    {
        Rect field = CodePageFieldBounds(size);
        int rowsBelow = Math.Max(0, size.Height - field.Bottom - 2);
        int rowsAbove = Math.Max(0, field.Y - 2);
        int available = Math.Max(rowsBelow, rowsAbove);
        return Math.Clamp(available, 3, 6);
    }

    private int MaxCodePageScrollTop(int contentRows) =>
        Math.Max(0, _codePages.Count - contentRows);

    private void EnsureCodePageVisible(int codePageIndex, ref int codePageScrollTop, int contentRows)
    {
        if (codePageIndex < codePageScrollTop)
            codePageScrollTop = codePageIndex;
        else if (codePageIndex >= codePageScrollTop + contentRows)
            codePageScrollTop = codePageIndex - contentRows + 1;

        codePageScrollTop = Math.Clamp(codePageScrollTop, 0, MaxCodePageScrollTop(contentRows));
    }

    private void DrawCodePageDropdown(
        ConsoleSize size,
        int codePageIndex,
        int codePageScrollTop,
        ref ScreenSnapshot? codePageDropdownUnderlay)
    {
        Rect bounds = CodePageDropdownBounds(size);
        codePageDropdownUnderlay ??= _screen.Capture(bounds);

        int contentRows = CodePageDropdownContentRows(size);
        var scrollState = new ScrollState
        {
            TotalItems = _codePages.Count,
            ViewportItems = contentRows,
            FirstVisibleIndex = codePageScrollTop,
        };
        var popupOptions = FarDialogStyles.FrameOptions with
        {
            DrawDoubleBorder = false,
            VerticalScrollState = scrollState,
        };

        new PopupRenderer().RenderPopup(_screen, bounds, popupOptions, (_, contentBounds) =>
        {
            int textWidth = Math.Max(1, contentBounds.Width - 1);
            for (int row = 0; row < contentRows; row++)
            {
                int itemIndex = codePageScrollTop + row;
                string text = itemIndex < _codePages.Count
                    ? Fit(_codePages[itemIndex].Label, textWidth)
                    : new string(' ', textWidth);
                _screen.Write(
                    contentBounds.X,
                    contentBounds.Y + row,
                    text,
                    itemIndex == codePageIndex ? FarDialogStyles.FocusedInput : FarDialogStyles.Input);
            }
        });
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
    }

    private static int Mod(int value, int size) => ((value % size) + size) % size;

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;
        return text.Length <= width ? text.PadRight(width) : text[..width];
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }
}
