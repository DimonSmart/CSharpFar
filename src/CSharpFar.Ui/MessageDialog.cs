using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Shows a simple message box and waits for Enter or Esc.</summary>
public sealed class MessageDialog
{
    private const int DialogWidth  = 52;
    private const int DialogHeight = 5;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public MessageDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show(string title, string message)
    {
        var size  = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            Draw(title, message, size);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var key = _screen.ReadKey();
                if (key.Key is ConsoleKey.Enter or ConsoleKey.Escape) break;
            }
        }
        finally
        {
            _screen.Restore(saved);
        }
    }

    public int ShowButtons(string title, string message, IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        var dialogButtons = buttons
            .Select((text, index) => new DialogButton(index.ToString(), text, HotKeyFrom(text), index == 0))
            .ToArray();
        var buttonBar = new DialogButtonBar(dialogButtons);
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));

        try
        {
            int focusedButton = 0;
            DrawButtons(title, message, size, buttonBar, focusedButton);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var input = _screen.ReadInput();
                if (buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
                {
                    if (buttonId is not null && int.TryParse(buttonId, out int selected))
                        return selected;

                    DrawButtons(title, message, size, buttonBar, focusedButton);
                    continue;
                }

                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
                    return -1;

                DrawButtons(title, message, size, buttonBar, focusedButton);
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(string title, string message, ConsoleSize size)
    {
        int dlgX = Math.Max(0, (size.Width  - DialogWidth)  / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        int fw   = DialogWidth - 4;

        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, Truncate(message, fw).PadRight(fw), PaletteStyles.DialogError(_palette));

            const string hint = "[ Press Enter ]";
            _screen.Write(dlgX + (DialogWidth - hint.Length) / 2, dlgY + 3, hint, PaletteStyles.DialogFill(_palette));
        });
    }

    private void DrawButtons(
        string title,
        string message,
        ConsoleSize size,
        DialogButtonBar buttonBar,
        int focusedButton)
    {
        int width = Math.Min(Math.Max(DialogWidth, message.Length + 6), Math.Max(DialogWidth, size.Width));
        int height = 7;
        int dlgX = Math.Max(0, (size.Width - width) / 2);
        int dlgY = Math.Max(0, (size.Height - height) / 2);
        int contentWidth = Math.Max(1, width - 4);

        var bounds = new Rect(dlgX, dlgY, width, height);
        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), (_, _) =>
        {
            _screen.Write(dlgX + 2, dlgY + 1, Truncate(message, contentWidth).PadRight(contentWidth), PaletteStyles.DialogError(_palette));
            buttonBar.Render(
                _screen,
                dlgX + 2,
                dlgY + height - 2,
                contentWidth,
                focusedButton,
                PaletteStyles.DialogFill(_palette),
                PaletteStyles.InputField(_palette));
        });
    }

    private static char HotKeyFrom(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
                return c;
        }

        return text.Length == 0 ? '\0' : text[0];
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : "\u2026" + s[^(maxLen - 1)..];
}
