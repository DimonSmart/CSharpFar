using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Asks the user to confirm a destructive action. Returns true if confirmed.</summary>
public sealed class ConfirmDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 7;

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok",     "OK",     'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public ConfirmDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    /// <summary>
    /// Draws the dialog and waits for input. Returns true if confirmed.
    /// </summary>
    public bool Show(string title, string question, string itemName)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(title, question, itemName, size);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private bool RunLoop(string title, string question, string itemName, ConsoleSize size)
    {
        int focusedButton = 0;
        Draw(title, question, itemName, size, focusedButton);

        while (true)
        {
            var input = _screen.ReadInput();

            if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel") return false;
                if (buttonId == "ok")     return true;
                Draw(title, question, itemName, size, focusedButton);
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
            {
                Draw(title, question, itemName, size, focusedButton);
                continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape: return false;
                case ConsoleKey.Enter:  return focusedButton == 0;
            }

            Draw(title, question, itemName, size, focusedButton);
        }
    }

    private void Draw(string title, string question, string itemName, ConsoleSize size, int focusedButton)
    {
        using var frame = _screen.BeginFrame();

        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        var outerBounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);

        _modalRenderer.Render(_screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            _screen.Write(contentX, bounds.Y + 1, Truncate(question, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

            string truncatedName = Truncate(itemName, contentWidth);
            int nameX = contentX + Math.Max(0, (contentWidth - truncatedName.Length) / 2);
            _screen.Write(contentX, bounds.Y + 2, new string(' ', contentWidth), FarDialogStyles.Fill);
            _screen.Write(nameX, bounds.Y + 2, truncatedName, FarDialogStyles.Fill);

            _buttonBar.Render(
                _screen,
                contentX,
                bounds.Y + bounds.Height - 2,
                contentWidth,
                focusedButton,
                FarDialogStyles.Fill,
                FarDialogStyles.FocusedInput);
        });
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
