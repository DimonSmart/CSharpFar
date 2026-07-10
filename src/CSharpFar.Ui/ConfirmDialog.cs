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
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok",     "OK",     'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public ConfirmDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
        _screen = modalDialogs.Screen;
    }

    /// <summary>
    /// Draws the dialog and waits for input. Returns true if confirmed.
    /// </summary>
    public bool Show(string title, string question, string itemName)
    {
        return ShowComposed(title, question, itemName);
    }

    private bool ShowComposed(string title, string question, string itemName)
    {
        int focusedButton = 0;
        using var session = _modalDialogs.Open(context =>
            RenderLayer(context.Screen, title, question, itemName, context.Size, focusedButton));

        while (true)
        {
            session.Render();
            var input = session.ReadInput();
            if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                if (buttonId == "cancel") return false;
                if (buttonId == "ok") return true;
                continue;
            }

            if (input is KeyConsoleInputEvent { Key: var key })
            {
                if (key.Key == ConsoleKey.Escape) return false;
                if (key.Key == ConsoleKey.Enter) return focusedButton == 0;
            }
        }
    }

    private void RenderLayer(ScreenRenderer screen, string title, string question, string itemName, ConsoleSize size, int focusedButton)
    {

        var outerBounds = _modalRenderer.CenteredOuterBounds(size, DialogWidth, DialogHeight, minWidth: 20, minHeight: 5);

        _modalRenderer.Render(screen, outerBounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);

            screen.Write(contentX, bounds.Y + 1, Truncate(question, contentWidth).PadRight(contentWidth), FarDialogStyles.Fill);

            string truncatedName = Truncate(itemName, contentWidth);
            int nameX = contentX + Math.Max(0, (contentWidth - truncatedName.Length) / 2);
            screen.Write(contentX, bounds.Y + 2, new string(' ', contentWidth), FarDialogStyles.Fill);
            screen.Write(nameX, bounds.Y + 2, truncatedName, FarDialogStyles.Fill);

            _buttonBar.Render(
                screen,
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
