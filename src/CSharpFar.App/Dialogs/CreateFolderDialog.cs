using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

internal sealed class CreateFolderDialog
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 9;
    private const string Title = "Make folder";
    private const string Prompt = "Create the folder:";

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public CreateFolderDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public string? Show(string? initialText = null, Func<string, string?>? validate = null)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(size, initialText, validate);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private string? RunLoop(ConsoleSize size, string? initialText, Func<string, string?>? validate)
    {
        var folderName = new CommandLineState();
        if (initialText is not null)
            folderName.SetText(initialText);

        string? error = null;
        int focusRow = 0;
        int focusedButton = 0;

        Draw(size, folderName, error, focusRow, focusedButton);

        while (true)
        {
            var input = _screen.ReadInput();

            if (focusRow == 1 &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "ok")
                {
                    string? result = TrySubmit(folderName, validate, ref error);
                    if (result is not null)
                        return result;
                }

                Draw(size, folderName, error, focusRow, focusedButton);
                continue;
            }

            if (input is MouseConsoleInputEvent &&
                _buttonBar.TryHandleInput(input, ref focusedButton, out buttonId) &&
                buttonId is not null)
            {
                focusRow = 1;
                if (buttonId == "cancel")
                    return null;

                string? result = TrySubmit(folderName, validate, ref error);
                if (result is not null)
                    return result;

                Draw(size, folderName, error, focusRow, focusedButton);
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                Draw(size, folderName, error, focusRow, focusedButton);
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    {
                        string? result = TrySubmit(folderName, validate, ref error);
                        if (result is not null)
                            return result;
                        break;
                    }
                case ConsoleKey.Enter:
                    if (focusRow == 1 && focusedButton == 1)
                        return null;

                    {
                        string? result = TrySubmit(folderName, validate, ref error);
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
                        EditText(folderName, key, ref error);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == 1)
                    {
                        if (focusedButton == 1)
                            return null;

                        string? result = TrySubmit(folderName, validate, ref error);
                        if (result is not null)
                            return result;
                    }
                    else
                    {
                        EditText(folderName, key, ref error);
                    }
                    break;
                default:
                    if (focusRow == 0)
                        EditText(folderName, key, ref error);
                    break;
            }

            Draw(size, folderName, error, focusRow, focusedButton);
        }
    }

    private static string? TrySubmit(
        CommandLineState folderName,
        Func<string, string?>? validate,
        ref string? error)
    {
        string text = folderName.Text.Trim();
        if (text.Length == 0)
            return null;

        error = validate?.Invoke(text);
        return error is null ? text : null;
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

    private void Draw(
        ConsoleSize size,
        CommandLineState folderName,
        string? error,
        int focusRow,
        int focusedButton)
    {
        using var frame = _screen.BeginFrame();

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
            DrawInput(contentX, inputY, contentWidth, folderName, focusRow == 0);
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
                focusRow == 1 ? FarDialogStyles.Input : FarDialogStyles.Fill);
        });

        if (focusRow == 0)
            SetInputCursor(outerBounds.X + 3, outerBounds.Y + 3, Math.Max(1, outerBounds.Width - 6), folderName);
        else
            _screen.SetCursorVisible(false);
    }

    private void DrawInput(int x, int y, int width, CommandLineState buffer, bool focused)
    {
        string text = VisibleInputText(buffer, width);
        _screen.Write(x, y, text.PadRight(width), focused ? FarDialogStyles.Input : FarDialogStyles.Fill);
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

    private void DrawSeparator(Rect bounds, int y)
    {
        if (y <= bounds.Y || y >= bounds.Bottom - 1)
            return;

        _screen.WriteChar(bounds.X, y, '╠', FarDialogStyles.Border);
        _screen.Write(bounds.X + 1, y, new string('═', Math.Max(0, bounds.Width - 2)), FarDialogStyles.Border);
        _screen.WriteChar(bounds.Right - 1, y, '╣', FarDialogStyles.Border);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "\u2026";
    }
}
