using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 9;
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly ModalDialogHost _modalDialogs;
    private readonly ScreenRenderer _screen;
    private ConsoleSize? _lastSize;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public CreateFolderDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
        _screen = modalDialogs.Screen;
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        return RunLoop(initialText, validate);
    }

    private string? RunLoop(string? initialText, Func<string, string?>? validate)
    {
        var folderName = new CommandLineState();
        if (initialText is not null)
            folderName.SetText(initialText);
        SingleLineTextHistoryState history = HistoryRegistry.GetOrCreate("CreateFolderDialog.FolderName");

        string? error = null;
        int focusRow = 0;
        int focusedButton = 0;
        ScrollBarDragState? historyScrollbarDrag = null;

        using var modal = _modalDialogs.Open(context =>
        {
            _lastSize = context.Size;
            Draw(context.Size, folderName, history, error, focusRow, focusedButton);
        });
        modal.Render();

        while (true)
        {
            var input = modal.ReadInput();

            if (focusRow == 1 &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "ok")
                {
                    string? result = TrySubmit(folderName, history, validate, ref error);
                    if (result is not null)
                        return result;
                }

                modal.Render();
                continue;
            }

            if (input is MouseConsoleInputEvent dropdownMouse &&
                _lastSize is { } size &&
                TryHandleHistoryDropdownMouse(dropdownMouse, size, folderName, history, ref historyScrollbarDrag))
            {
                focusRow = 0;
                modal.Render();
                continue;
            }

            if (input is MouseConsoleInputEvent mouse &&
                _lastSize is { } layoutSize && TryHandleHistoryArrow(mouse, layoutSize, history))
            {
                focusRow = 0;
                modal.Render();
                continue;
            }

            if (input is MouseConsoleInputEvent &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                buttonId is not null)
            {
                focusRow = 1;
                if (buttonId == "cancel")
                    return null;

                string? result = TrySubmit(folderName, history, validate, ref error);
                if (result is not null)
                    return result;

                modal.Render();
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                modal.Render();
                continue;
            }

            if (_lastSize is not { } currentSize)
                continue;

            int availableRows = SingleLineTextInput.AvailableDropdownContentRows(InputFieldY(currentSize), currentSize.Height);
            if (focusRow == 0 &&
                history.IsDropdownOpen &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                SingleLineTextInput.HandleKey(folderName, key, ref error, history, availableRows);
                modal.Render();
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    {
                        string? result = TrySubmit(folderName, history, validate, ref error);
                        if (result is not null)
                            return result;
                        break;
                    }
                case ConsoleKey.Enter:
                    if (focusRow == 1 && focusedButton == 1)
                        return null;

                    {
                        string? result = TrySubmit(folderName, history, validate, ref error);
                        if (result is not null)
                            return result;
                        break;
                    }
                case ConsoleKey.Tab:
                    focusRow = focusRow == 0 ? 1 : 0;
                    break;
                case ConsoleKey.DownArrow:
                    focusRow = 1;
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = 0;
                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    if (focusRow == 1)
                        _buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        EditText(folderName, key, history, availableRows, ref error);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                    {
                        if (focusedButton == 1)
                            return null;

                        string? result = TrySubmit(folderName, history, validate, ref error);
                        if (result is not null)
                            return result;
                    }
                    else
                    {
                        EditText(folderName, key, history, availableRows, ref error);
                    }
                    break;
                default:
                    if (focusRow == 0)
                        EditText(folderName, key, history, availableRows, ref error);
                    break;
            }

            modal.Render();
        }
    }

    private static string? TrySubmit(
        CommandLineState folderName,
        SingleLineTextHistoryState history,
        Func<string, string?>? validate,
        ref string? error)
    {
        string text = folderName.Text.Trim();
        if (text.Length == 0)
            return null;

        error = validate?.Invoke(text);
        if (error is not null)
            return null;

        history.Add(text);
        return text;
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
        CommandLineState folderName,
        SingleLineTextHistoryState history,
        string? error,
        int focusRow,
        int focusedButton)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        var outerBounds = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);

        _modalRenderer.Render(_screen, outerBounds, Title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int inputY = bounds.Y + 2;

            _screen.Write(contentX, bounds.Y + 1, Prompt.PadRight(contentWidth), FarDialogStyles.Fill);
            DrawInput(contentX, inputY, contentWidth, folderName, history, focusRow == 0);
            DrawSeparator(bounds, bounds.Y + 3);

            string errorText = error is null ? string.Empty : Truncate(error, contentWidth);
            _screen.Write(contentX, bounds.Y + 4, errorText.PadRight(contentWidth), FarDialogStyles.Error);

            _buttonBar.Render(
                _screen,
                contentX,
                bounds.Y + bounds.Height - 2,
                contentWidth,
                focusedButton,
                FarDialogStyles.Fill,
                focusRow == 1 ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill);
        });

        if (focusRow == 0)
        {
            int inputX = outerBounds.X + 3;
            int inputY = outerBounds.Y + 3;
            int inputWidth = Math.Max(1, outerBounds.Width - 6);
            SingleLineTextInput.RenderHistoryDropdown(_screen, inputX, inputY, inputWidth, history);
        }

        if (focusRow == 0)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 3, Math.Max(1, outerBounds.Width - 6), folderName, hasHistory: true);
        else
            _screen.SetCursorVisible(false);
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

    private static bool TryHandleHistoryArrow(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        SingleLineTextHistoryState history)
    {
        int fieldX = InputFieldX(size);
        int fieldY = InputFieldY(size);
        int fieldWidth = InputFieldWidth(size);
        if (!SingleLineTextInput.IsHistoryArrowHit(fieldX, fieldWidth, fieldY, mouse.X, mouse.Y))
            return false;

        return SingleLineTextInput.TryOpenHistoryDropdown(history, fieldY, size.Height);
    }

    private static bool TryHandleHistoryDropdownMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        CommandLineState folderName,
        SingleLineTextHistoryState history,
        ref ScrollBarDragState? historyScrollbarDrag) =>
        SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            folderName,
            mouse,
            InputFieldX(size),
            InputFieldY(size),
            InputFieldWidth(size),
            size.Height,
            ref historyScrollbarDrag);

    private static int InputFieldX(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        int dialogX = Math.Max(0, (size.Width - dialogWidth) / 2);
        return dialogX + 3;
    }

    private static int InputFieldY(ConsoleSize size)
    {
        int dialogHeight = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int dialogY = Math.Max(0, (size.Height - dialogHeight) / 2);
        return dialogY + 3;
    }

    private static int InputFieldWidth(ConsoleSize size)
    {
        int dialogWidth = Math.Min(DialogWidth, Math.Max(40, size.Width - 2));
        return Math.Max(1, dialogWidth - 6);
    }

    private void DrawSeparator(Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        _screen.WriteChar(bounds.X, y, '╟', FarDialogStyles.Border);
        _screen.Write(bounds.X + 1, y, new string('─', Math.Max(0, bounds.Width - 2)), FarDialogStyles.Border);
        _screen.WriteChar(bounds.Right - 1, y, '╢', FarDialogStyles.Border);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }
}
