using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Shows a message box and waits for Enter or Esc.</summary>
public sealed class MessageDialog
{
    private const int MinDialogWidth = 52;
    private const int MaxDialogWidth = 96;

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
            var layout = CreateLayout(title, message, size, buttons: null);
            int firstVisibleLine = 0;
            Draw(title, layout, firstVisibleLine, buttonBar: null, focusedButton: 0);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var input = _screen.ReadInput();
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter or ConsoleKey.Escape })
                    break;

                if (TryScroll(input, layout, ref firstVisibleLine))
                    Draw(title, layout, firstVisibleLine, buttonBar: null, focusedButton: 0);
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
            var layout = CreateLayout(title, message, size, dialogButtons);
            int focusedButton = 0;
            int firstVisibleLine = 0;
            Draw(title, layout, firstVisibleLine, buttonBar, focusedButton);
            _screen.SetCursorVisible(false);

            while (true)
            {
                var input = _screen.ReadInput();
                if (TryScroll(input, layout, ref firstVisibleLine))
                {
                    Draw(title, layout, firstVisibleLine, buttonBar, focusedButton);
                    continue;
                }

                if (buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
                {
                    if (buttonId is not null && int.TryParse(buttonId, out int selected))
                        return selected;

                    Draw(title, layout, firstVisibleLine, buttonBar, focusedButton);
                    continue;
                }

                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
                    return -1;

                Draw(title, layout, firstVisibleLine, buttonBar, focusedButton);
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(
        string title,
        MessageDialogLayout layout,
        int firstVisibleLine,
        DialogButtonBar? buttonBar,
        int focusedButton)
    {
        var scrollState = layout.MessageLines.Count > layout.ContentHeight
            ? new ScrollState
            {
                TotalItems = layout.MessageLines.Count,
                ViewportItems = layout.ContentHeight,
                FirstVisibleIndex = firstVisibleLine,
            }
            : null;

        new DialogFrameRenderer().RenderFrame(_screen, layout.Bounds, title, false, PaletteStyles.DialogPopupOptions(_palette), scrollState, (_, contentBounds) =>
        {
            int textX = contentBounds.X + 1;
            int textWidth = Math.Max(1, contentBounds.Width - 2);
            for (int row = 0; row < layout.ContentHeight; row++)
            {
                int lineIndex = firstVisibleLine + row;
                string text = lineIndex < layout.MessageLines.Count
                    ? layout.MessageLines[lineIndex]
                    : string.Empty;
                _screen.Write(
                    textX,
                    contentBounds.Y + row,
                    Fit(text, textWidth),
                    PaletteStyles.DialogError(_palette));
            }

            if (buttonBar is null)
            {
                const string hint = "[ Press Enter ]";
                _screen.Write(
                    layout.Bounds.X + Math.Max(0, (layout.Bounds.Width - hint.Length) / 2),
                    layout.ActionRow,
                    hint,
                    PaletteStyles.DialogFill(_palette));
                return;
            }

            buttonBar.Render(
                _screen,
                textX,
                layout.ActionRow,
                textWidth,
                focusedButton,
                PaletteStyles.DialogFill(_palette),
                PaletteStyles.InputField(_palette));
        });
    }

    private static MessageDialogLayout CreateLayout(
        string title,
        string message,
        ConsoleSize size,
        IReadOnlyList<DialogButton>? buttons)
    {
        int availableWidth = Math.Max(1, size.Width - 2);
        int rawTextWidth = LongestRawLine(message);
        int buttonWidth = buttons is null ? "[ Press Enter ]".Length : ButtonRowWidth(buttons);
        int titleWidth = string.IsNullOrEmpty(title) ? 0 : title.Length + 2;
        int desiredWidth = Math.Max(MinDialogWidth, Math.Max(Math.Max(rawTextWidth, buttonWidth), titleWidth) + 4);
        int width = Math.Min(Math.Min(MaxDialogWidth, desiredWidth), availableWidth);
        int textWidth = Math.Max(1, width - 4);
        List<string> messageLines = WrapMessage(message, textWidth);

        int availableHeight = Math.Max(1, size.Height - 2);
        int maxContentHeight = Math.Max(1, availableHeight - 4);
        int contentHeight = Math.Min(messageLines.Count, maxContentHeight);
        int height = Math.Min(availableHeight, contentHeight + 4);
        contentHeight = Math.Max(1, height - 4);

        int dlgX = Math.Max(0, (size.Width - width) / 2);
        int dlgY = Math.Max(0, (size.Height - height) / 2);

        return new MessageDialogLayout(
            new Rect(dlgX, dlgY, width, height),
            messageLines,
            contentHeight,
            dlgY + height - 2);
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

    private static bool TryScroll(
        ConsoleInputEvent input,
        MessageDialogLayout layout,
        ref int firstVisibleLine)
    {
        if (layout.MessageLines.Count <= layout.ContentHeight)
            return false;

        if (input is not KeyConsoleInputEvent { Key: var key })
            return false;

        int previous = firstVisibleLine;
        int maxFirstVisible = Math.Max(0, layout.MessageLines.Count - layout.ContentHeight);
        firstVisibleLine = key.Key switch
        {
            ConsoleKey.UpArrow => Math.Max(0, firstVisibleLine - 1),
            ConsoleKey.DownArrow => Math.Min(maxFirstVisible, firstVisibleLine + 1),
            ConsoleKey.PageUp => Math.Max(0, firstVisibleLine - layout.ContentHeight),
            ConsoleKey.PageDown => Math.Min(maxFirstVisible, firstVisibleLine + layout.ContentHeight),
            ConsoleKey.Home => 0,
            ConsoleKey.End => maxFirstVisible,
            _ => firstVisibleLine,
        };

        return firstVisibleLine != previous;
    }

    private static List<string> WrapMessage(string message, int width)
    {
        width = Math.Max(1, width);
        string normalized = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var result = new List<string>();
        foreach (string rawLine in normalized.Split('\n'))
            WrapRawLine(rawLine, width, result);

        if (result.Count == 0)
            result.Add(string.Empty);

        return result;
    }

    private static void WrapRawLine(string rawLine, int width, List<string> result)
    {
        if (rawLine.Length == 0)
        {
            result.Add(string.Empty);
            return;
        }

        string remaining = rawLine;
        while (remaining.Length > width)
        {
            int breakAt = remaining.LastIndexOf(' ', width - 1, width);
            if (breakAt <= 0)
                breakAt = width;

            string line = remaining[..breakAt].TrimEnd();
            result.Add(line.Length == 0 ? remaining[..breakAt] : line);
            remaining = remaining[breakAt..].TrimStart();
            if (remaining.Length == 0)
                return;
        }

        result.Add(remaining);
    }

    private static int LongestRawLine(string message)
    {
        string normalized = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return normalized.Split('\n').DefaultIfEmpty(string.Empty).Max(line => line.Length);
    }

    private static int ButtonRowWidth(IReadOnlyList<DialogButton> buttons) =>
        buttons.Sum(button => FormatButtonLength(button.Text, button.IsDefault)) + Math.Max(0, buttons.Count - 1);

    private static int FormatButtonLength(string text, bool isDefault) =>
        text.Length + (isDefault ? 4 : 4);

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width
            ? text.PadRight(width)
            : text[..width];
    }

    private sealed record MessageDialogLayout(
        Rect Bounds,
        List<string> MessageLines,
        int ContentHeight,
        int ActionRow);
}
