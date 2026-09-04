using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Shows a message box with a standard action that can be activated by keyboard or mouse.</summary>
public sealed class MessageDialog
{
    private static readonly UiTargetScope Targets = new("message-dialog");
    private static readonly UiTargetId ContentTarget = Targets.Child("content");
    private static readonly UiTargetId ScrollbarTarget = Targets.Child("scrollbar");
    private const int MinDialogWidth = 52;
    private const int MaxDialogWidth = 96;

    private readonly ModalDialogHost _modalDialogs;

    public MessageDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public void Show(string title, string message)
    {
        _ = ShowCore(
            title,
            message,
            [new DialogButton("ok", "OK", 'O', IsDefault: true)]);
    }

    public int ShowButtons(string title, string message, IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        var dialogButtons = buttons
            .Select((text, index) => new DialogButton(index.ToString(), text, HotKeyFrom(text), index == 0))
            .ToArray();
        return ShowCore(title, message, dialogButtons);
    }

    private int ShowCore(string title, string message, IReadOnlyList<DialogButton> buttons)
    {
        var actions = new DialogActionController(buttons, 0, null);
        var viewport = CreateViewport();
        return _modalDialogs.RunInteractive<MessageDialogFrame, DialogActionOutcome?, int>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(title, message, context.Size, buttons);
                return Draw(context, focusScope, title, layout, viewport, actions);
            },
            BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key: var key } &&
                    IsScrollable(frame.Viewport) &&
                    IsViewportScrollKey(key))
                {
                    return (default(DialogActionOutcome?), viewport.RouteInput(input, frame.Viewport, route).UiResult);
                }

                if (input is MouseConsoleInputEvent && viewport.IsTargetRoute(route))
                    return (default(DialogActionOutcome?), viewport.RouteInput(input, frame.Viewport, route).UiResult);

                FormRouteResult result = actions.RouteInput(input, frame.Buttons, route);
                return (actions.Interpret(result.FormResult), result.UiResult);
            },
            (_, outcome) =>
            {
                if (outcome is { } action)
                {
                    return ModalDialogLoopResult<int>.Complete(
                        action.Kind == DialogActionOutcomeKind.Activated ? action.ButtonIndex : -1);
                }

                return ModalDialogLoopResult<int>.ContinueNoChange;
            },
            applyCommittedFrame: frame => viewport.ApplyCommittedFrame(frame.Viewport));
    }

    private static UiInteractionFrame BuildInteractionFrame(MessageDialogFrame frame) =>
        new UiInteractionFrameBuilder()
            .AddFragment(frame.ViewportControl.BuildInteractionFragment(frame.Viewport))
            .AddFragment(frame.Actions.BuildInteractionFragment(frame.Buttons))
            .SetDefaultFocusTarget(frame.Buttons.DefaultTarget)
            .Build();

    private MessageDialogFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        string title,
        MessageDialogLayout layout,
        RoutedScrollableViewport viewport,
        DialogActionController actions)
    {
        ScrollableFormFrame? buttons = null;
        IUiCanvas screen = context.Canvas;
        Rect contentBounds = PopupRenderer.GetContentBounds(layout.Bounds, drawBorder: true);
        var textBounds = new Rect(contentBounds.X + 1, contentBounds.Y, Math.Max(1, contentBounds.Width - 2), layout.ContentHeight);
        Rect? scrollbarBounds = layout.MessageLines.Count > layout.ContentHeight
            ? new Rect(layout.Bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height)
            : null;
        ScrollableViewportFrameState viewportFrame = viewport.CalculateFrame(
            layout.MessageLines.Count, layout.ContentHeight, textBounds, scrollbarBounds);
        ScrollState? scrollState = viewport.GetScrollState(viewportFrame);

        new DialogFrameRenderer().RenderFrame(screen, layout.Bounds, title, false, DialogStyles.PopupOptions, scrollState, (_, contentBounds) =>
        {
            int textX = viewportFrame.ContentBounds.X;
            int textWidth = viewportFrame.ContentBounds.Width;
            for (int row = 0; row < layout.ContentHeight; row++)
            {
                int lineIndex = viewportFrame.FirstVisibleIndex + row;
                string text = lineIndex < layout.MessageLines.Count
                    ? layout.MessageLines[lineIndex]
                    : string.Empty;
                screen.Write(
                    textX,
                    contentBounds.Y + row,
                    Fit(text, textWidth),
                    DialogStyles.Fill);
            }

            buttons = actions.Render(
                new FormRenderContext(
                    context,
                    new Rect(textX, layout.ActionRow, textWidth, 1),
                    DialogStyles.Border,
                    new Rect(textX, layout.ActionRow, textWidth, 1)),
                focusScope);
        });

        return new MessageDialogFrame(
            layout,
            viewportFrame,
            viewport,
            buttons ?? throw new InvalidOperationException("Message dialog did not render its action buttons."),
            actions);
    }

    private static MessageDialogLayout CreateLayout(
        string title,
        string message,
        ConsoleSize size,
        IReadOnlyList<DialogButton> buttons)
    {
        int availableWidth = Math.Max(1, size.Width - 2);
        int rawTextWidth = LongestRawLine(message);
        int buttonWidth = DialogButtonBar.MeasureWidth(buttons);
        int titleWidth = string.IsNullOrEmpty(title) ? 0 : ConsoleTextMetrics.GetCellWidth(title) + 2;
        int desiredWidth = Math.Max(MinDialogWidth, Math.Max(Math.Max(rawTextWidth, buttonWidth), titleWidth) + 4);
        int width = Math.Min(Math.Min(MaxDialogWidth, desiredWidth), availableWidth);
        int textWidth = Math.Max(1, width - 4);
        var messageLines = Array.AsReadOnly(WrapMessage(message, textWidth).ToArray());

        int availableHeight = Math.Max(1, size.Height - 2);
        int maxContentHeight = Math.Max(1, availableHeight - 4);
        int contentHeight = Math.Min(messageLines.Count, maxContentHeight);
        int height = Math.Min(availableHeight, contentHeight + 4);
        contentHeight = Math.Max(1, height - 4);

        Rect bounds = UiLayout.Center(size, width, height);

        return new MessageDialogLayout(
            bounds,
            messageLines,
            contentHeight,
            bounds.Bottom - 2);
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

    private static RoutedScrollableViewport CreateViewport() =>
        new(ContentTarget, ScrollbarTarget);

    private static bool IsViewportScrollKey(ConsoleKeyInfo key) =>
        key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or
            ConsoleKey.PageDown or ConsoleKey.Home or ConsoleKey.End;

    private static bool IsScrollable(ScrollableViewportFrameState frame) =>
        frame.TotalItems > frame.ViewportItems;

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
        while (ConsoleTextMetrics.GetCellWidth(remaining) > width)
        {
            string visible = ConsoleTextMetrics.TruncateToCells(remaining, width);
            int breakAt = visible.LastIndexOf(' ');
            if (breakAt <= 0)
                breakAt = visible.Length;

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
        return normalized.Split('\n').DefaultIfEmpty(string.Empty).Max(ConsoleTextMetrics.GetCellWidth);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return ConsoleTextMetrics.FitToCells(text, width);
    }

    private sealed record MessageDialogLayout(
        Rect Bounds,
        IReadOnlyList<string> MessageLines,
        int ContentHeight,
        int ActionRow);

    private readonly record struct MessageDialogFrame(
        MessageDialogLayout Layout,
        ScrollableViewportFrameState Viewport,
        RoutedScrollableViewport ViewportControl,
        ScrollableFormFrame Buttons,
        DialogActionController Actions);
}
