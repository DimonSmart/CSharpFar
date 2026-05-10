using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Dialogs;

internal sealed class OperationCancelDialog
{
    private const int DialogWidth = 46;
    private const int DialogHeight = 8;
    private const string YesButton = "yes";
    private const string NoButton = "no";

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttons = new(
    [
        new DialogButton(YesButton, "Yes", 'Y', IsDefault: true),
        new DialogButton(NoButton, "No", 'N'),
    ]);

    public OperationCancelDialog(ScreenRenderer screen)
    {
        _screen = screen;
    }

    public bool Show(
        string interruptedMessage = "Operation has been interrupted",
        string confirmationMessage = "Do you really want to cancel it?")
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        int focusedButton = 0;

        try
        {
            while (true)
            {
                Draw(size, focusedButton, interruptedMessage, confirmationMessage);

                var input = _screen.ReadInput();
                if (_buttons.TryHandleInput(input, ref focusedButton, out string? buttonId) && buttonId is not null)
                    return buttonId == YesButton;

                if (input is KeyConsoleInputEvent { Key: var key })
                {
                    if (key.Key == ConsoleKey.Escape)
                        return false;
                    if (key.Key == ConsoleKey.Tab)
                        focusedButton = (focusedButton + 1) % _buttons.Count;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(
        ConsoleSize size,
        int focusedButton,
        string interruptedMessage,
        string confirmationMessage)
    {
        int x = Math.Max(0, (size.Width - DialogWidth) / 2);
        int y = Math.Max(0, (size.Height - DialogHeight) / 2);
        var bounds = new Rect(x, y, DialogWidth, DialogHeight);

        using var frame = _screen.BeginFrame();

        _modalRenderer.Render(_screen, bounds, string.Empty, true, WarningDialogStyles.OuterOptions, WarningDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect contentBounds = layout.ContentBounds;
            int contentX = contentBounds.X + 1;
            int contentWidth = Math.Max(1, contentBounds.Width - 2);

            _screen.Write(contentX, contentBounds.Y, Center(interruptedMessage, contentWidth), WarningDialogStyles.Fill);
            _screen.Write(contentX, contentBounds.Y + 1, Center(confirmationMessage, contentWidth), WarningDialogStyles.Fill);

            _buttons.Render(
                _screen,
                contentX,
                contentBounds.Bottom - 1,
                contentWidth,
                focusedButton,
                WarningDialogStyles.Fill,
                WarningDialogStyles.ButtonFocus);
        });

        _screen.SetCursorVisible(false);
    }

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        int left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }

}
