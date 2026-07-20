using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Shows a message box and waits for Enter or Esc.</summary>
public sealed class MessageDialog
{
    private static readonly UiTargetId DialogTarget = new("message-dialog");
    private const int MinDialogWidth = 52;
    private const int MaxDialogWidth = 96;

    private readonly ModalDialogHost _modalDialogs;

    public MessageDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public void Show(string title, string message)
    {
        int firstVisibleLine = 0;
        _modalDialogs.RunInteractive<MessageDialogFrame, MessageDialogInput, Unit>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(title, message, context.Size, buttons: null);
                int effectiveFirstVisibleLine = NormalizeScroll(layout, firstVisibleLine);
                return Draw(context, focusScope, title, layout, effectiveFirstVisibleLine, form: null);
            },
            BuildInteractionFrame,
            static (input, _, _) => (new MessageDialogInput(input, FormInputResult.NotHandled), UiInputResult.HandledResult),
            (routed, semantic) =>
            {
                ConsoleInputEvent input = semantic.Input;
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter or ConsoleKey.Escape })
                    return ModalDialogLoopResult<Unit>.Complete(default);
                TryScroll(input, routed.Frame, ref firstVisibleLine);
                return ModalDialogLoopResult<Unit>.Continue;
            },
            applyCommittedFrame: frame => firstVisibleLine = frame.FirstVisibleLine);
    }

    public int ShowButtons(string title, string message, IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));
        var dialogButtons = buttons
            .Select((text, index) => new DialogButton(index.ToString(), text, HotKeyFrom(text), index == 0))
            .ToArray();
        var form = new ScrollableFormDialog([
            new ButtonRow(dialogButtons, FarDialogStyles.Fill, FarDialogStyles.FocusedInput) { Id = "actions" },
        ]);
        int firstVisibleLine = 0;
        return _modalDialogs.RunInteractive<MessageDialogFrame, MessageDialogInput, int>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(title, message, context.Size, dialogButtons);
                int effectiveFirstVisibleLine = NormalizeScroll(layout, firstVisibleLine);
                return Draw(context, focusScope, title, layout, effectiveFirstVisibleLine, form);
            },
            BuildInteractionFrame,
            (input, frame, route) =>
            {
                FormRouteResult result = form.RouteInput(input, frame.Buttons!, route);
                return (new MessageDialogInput(input, result.FormResult), result.UiResult);
            },
            (routed, semantic) =>
            {
                ConsoleInputEvent input = semantic.Input;
                if (TryScroll(input, routed.Frame, ref firstVisibleLine))
                    return ModalDialogLoopResult<int>.Continue;

                if (semantic.FormResult.Command is string buttonId && int.TryParse(buttonId, out int selected))
                {
                    return ModalDialogLoopResult<int>.Complete(selected);
                }
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
                    return ModalDialogLoopResult<int>.Complete(-1);

                return ModalDialogLoopResult<int>.Continue;
            },
            applyCommittedFrame: frame => firstVisibleLine = frame.FirstVisibleLine);
    }

    private static UiInteractionFrame BuildInteractionFrame(MessageDialogFrame frame) =>
        frame is { Form: not null, Buttons: not null }
            ? frame.Form.BuildInteractionFrame(frame.Buttons)
            : new UiInteractionFrame(
                [],
                new UiFocusFrame([new UiFocusEntry(DialogTarget, 0, Cursor: new UiCursorPlacement(0, 0, Visible: false))], DialogTarget),
                DialogTarget);

    private static int NormalizeScroll(MessageDialogLayout layout, int firstVisibleLine) =>
        Math.Clamp(firstVisibleLine, 0, Math.Max(0, layout.MessageLines.Count - layout.ContentHeight));

    private MessageDialogFrame Draw(
        UiRenderContext context,
        UiFocusScope focusScope,
        string title,
        MessageDialogLayout layout,
        int firstVisibleLine,
        ScrollableFormDialog? form)
    {
        ScrollableFormFrame? buttons = null;
        ScreenRenderer screen = context.Screen;
        var scrollState = layout.MessageLines.Count > layout.ContentHeight
            ? new ScrollState
            {
                TotalItems = layout.MessageLines.Count,
                ViewportItems = layout.ContentHeight,
                FirstVisibleIndex = firstVisibleLine,
            }
            : null;

        var palette = UiTheme.Current;
        new DialogFrameRenderer().RenderFrame(screen, layout.Bounds, title, false, PaletteStyles.DialogPopupOptions(palette), scrollState, (_, contentBounds) =>
        {
            int textX = contentBounds.X + 1;
            int textWidth = Math.Max(1, contentBounds.Width - 2);
            for (int row = 0; row < layout.ContentHeight; row++)
            {
                int lineIndex = firstVisibleLine + row;
                string text = lineIndex < layout.MessageLines.Count
                    ? layout.MessageLines[lineIndex]
                    : string.Empty;
                screen.Write(
                    textX,
                    contentBounds.Y + row,
                    Fit(text, textWidth),
                    PaletteStyles.DialogError(palette));
            }

            if (form is null)
            {
                const string hint = "[ Press Enter ]";
                screen.Write(
                    layout.Bounds.X + Math.Max(0, (layout.Bounds.Width - hint.Length) / 2),
                    layout.ActionRow,
                    hint,
                    PaletteStyles.DialogFill(palette));
                return;
            }

            buttons = form.Render(
                new FormRenderContext(
                    context,
                    new Rect(textX, layout.ActionRow, textWidth, 1),
                    PaletteStyles.DialogBorder(palette)),
                focusScope);
        });
        return new MessageDialogFrame(layout, firstVisibleLine, buttons, form);
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
        var messageLines = Array.AsReadOnly(WrapMessage(message, textWidth).ToArray());

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
        MessageDialogFrame frame,
        ref int firstVisibleLine)
    {
        var layout = frame.Layout;
        if (layout.MessageLines.Count <= layout.ContentHeight)
            return false;

        if (input is not KeyConsoleInputEvent { Key: var key })
            return false;

        int previous = firstVisibleLine;
        int maxFirstVisible = Math.Max(0, layout.MessageLines.Count - layout.ContentHeight);
        firstVisibleLine = key.Key switch
        {
            ConsoleKey.UpArrow => Math.Max(0, frame.FirstVisibleLine - 1),
            ConsoleKey.DownArrow => Math.Min(maxFirstVisible, frame.FirstVisibleLine + 1),
            ConsoleKey.PageUp => Math.Max(0, frame.FirstVisibleLine - layout.ContentHeight),
            ConsoleKey.PageDown => Math.Min(maxFirstVisible, frame.FirstVisibleLine + layout.ContentHeight),
            ConsoleKey.Home => 0,
            ConsoleKey.End => maxFirstVisible,
            _ => frame.FirstVisibleLine,
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
        IReadOnlyList<string> MessageLines,
        int ContentHeight,
        int ActionRow);

    private readonly record struct MessageDialogFrame(
        MessageDialogLayout Layout,
        int FirstVisibleLine,
        ScrollableFormFrame? Buttons,
        ScrollableFormDialog? Form);

    private readonly record struct MessageDialogInput(
        ConsoleInputEvent Input,
        FormInputResult FormResult);
}
