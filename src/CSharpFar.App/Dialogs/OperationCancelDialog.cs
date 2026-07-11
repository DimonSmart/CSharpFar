using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class OperationCancelDialog
{
    private const int DialogWidth = 46;
    private const int DialogHeight = 8;
    private const string YesButton = "yes";
    private const string NoButton = "no";

    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttons = new(
    [
        new DialogButton(YesButton, "Yes", 'Y', IsDefault: true),
        new DialogButton(NoButton, "No", 'N'),
    ]);

    public OperationCancelDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public bool Show(
        string interruptedMessage = "Operation has been interrupted",
        string confirmationMessage = "Do you really want to cancel it?")
    {
        int focusedButton = 0;
        using var modal = _modalDialogs.Open(context => Draw(context, focusedButton, interruptedMessage, confirmationMessage));
        while (true)
        {
            modal.Render();

            var input = modal.ReadInput(out var frame);
            if (_buttons.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId) && buttonId is not null)
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

    private OperationCancelFrame Draw(
        UiRenderContext context,
        int focusedButton,
        string interruptedMessage,
        string confirmationMessage)
    {
        DialogButtonBarLayout buttons = null!;
        int x = Math.Max(0, (context.Size.Width - DialogWidth) / 2);
        int y = Math.Max(0, (context.Size.Height - DialogHeight) / 2);
        var bounds = new Rect(x, y, DialogWidth, DialogHeight);

        _modalRenderer.Render(context.Screen, bounds, string.Empty, true, WarningDialogStyles.OuterOptions, WarningDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect contentBounds = layout.ContentBounds;
            int contentX = contentBounds.X + 1;
            int contentWidth = Math.Max(1, contentBounds.Width - 2);

            context.Screen.Write(contentX, contentBounds.Y, Center(interruptedMessage, contentWidth), WarningDialogStyles.Fill);
            context.Screen.Write(contentX, contentBounds.Y + 1, Center(confirmationMessage, contentWidth), WarningDialogStyles.Fill);

            buttons = _buttons.Render(
                context.Screen,
                contentX,
                contentBounds.Bottom - 1,
                contentWidth,
                focusedButton,
                WarningDialogStyles.Fill,
                WarningDialogStyles.ButtonFocus);
        });

        context.Screen.SetCursorVisible(false);
        return new OperationCancelFrame(buttons);
    }

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        int left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }

    private readonly record struct OperationCancelFrame(DialogButtonBarLayout Buttons);
}
