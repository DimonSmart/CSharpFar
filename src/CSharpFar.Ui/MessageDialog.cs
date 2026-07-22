using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Shows a message box and waits for Enter or Esc.</summary>
public sealed class MessageDialog
{
    private static readonly UiTargetId DialogTarget = new("message-dialog");
    private static readonly UiTargetId ContentTarget = new("message-dialog-content");
    private static readonly UiTargetId ScrollbarTarget = new("message-dialog-scrollbar");
    private const int MinDialogWidth = 52;
    private const int MaxDialogWidth = 96;

    private readonly ModalDialogHost _modalDialogs;

    public MessageDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));
    }

    public void Show(string title, string message)
    {
        var viewport = new ScrollableViewport();
        _modalDialogs.RunInteractive<MessageDialogFrame, MessageDialogInput, Unit>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(title, message, context.Size, buttons: null);
                return Draw(context, focusScope, title, layout, viewport, actions: null);
            },
            BuildInteractionFrame,
            (input, frame, route) => (new MessageDialogInput(input, FormInputResult.NotHandled), RouteViewportInput(input, frame, route, viewport)),
            (routed, semantic) =>
            {
                ConsoleInputEvent input = semantic.Input;
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter or ConsoleKey.Escape })
                    return ModalDialogLoopResult<Unit>.Complete(default);
                return ModalDialogLoopResult<Unit>.Continue;
            },
            applyCommittedFrame: frame => viewport.ApplyCommittedFrame(frame.Viewport));
    }

    public int ShowButtons(string title, string message, IReadOnlyList<string> buttons)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        if (buttons.Count == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));
        var dialogButtons = buttons
            .Select((text, index) => new DialogButton(index.ToString(), text, HotKeyFrom(text), index == 0))
            .ToArray();
        var actions = new DialogActionController(
            dialogButtons, 0, null, FarDialogStyles.Fill, FarDialogStyles.FocusedInput);
        var viewport = new ScrollableViewport();
        return _modalDialogs.RunInteractive<MessageDialogFrame, MessageDialogInput, int>(
            (context, focusScope) =>
            {
                var layout = CreateLayout(title, message, context.Size, dialogButtons);
                return Draw(context, focusScope, title, layout, viewport, actions);
            },
            BuildInteractionFrame,
            (input, frame, route) =>
            {
                if (input is KeyConsoleInputEvent { Key: var key } &&
                    IsScrollable(frame.Viewport) &&
                    IsViewportScrollKey(key))
                {
                    return (new MessageDialogInput(input, FormInputResult.NotHandled), RouteViewportKey(key, frame, viewport));
                }

                if (input is MouseConsoleInputEvent && IsViewportMouseRoute(route))
                    return (new MessageDialogInput(input, FormInputResult.NotHandled), RouteViewportMouse((MouseConsoleInputEvent)input, frame, viewport));

                FormRouteResult result = actions.RouteInput(input, frame.Buttons!, route);
                return (new MessageDialogInput(input, result.FormResult, actions.Interpret(result.FormResult)), result.UiResult);
            },
            (routed, semantic) =>
            {
                if (semantic.ActionOutcome is { } outcome)
                    return ModalDialogLoopResult<int>.Complete(outcome.Kind == DialogActionOutcomeKind.Activated ? outcome.ButtonIndex : -1);

                return ModalDialogLoopResult<int>.Continue;
            },
            applyCommittedFrame: frame => viewport.ApplyCommittedFrame(frame.Viewport));
    }

    private static UiInteractionFrame BuildInteractionFrame(MessageDialogFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddHitRegion(ContentTarget, frame.Viewport.ContentBounds);
        if (frame.Viewport.ScrollbarBounds is Rect scrollbar)
            builder.AddHitRegion(ScrollbarTarget, scrollbar);
        if (frame is { Actions: not null, Buttons: not null })
            return builder
                .AddFragment(frame.Actions.BuildInteractionFragment(frame.Buttons))
                .SetDefaultFocusTarget(frame.Buttons.DefaultTarget)
                .Build();

        return builder
            .AddFocusEntry(DialogTarget, 0, cursor: new UiCursorPlacement(0, 0, Visible: false))
            .SetDefaultFocusTarget(DialogTarget)
            .SetKeyboardTarget(DialogTarget)
            .Build();
    }

    private MessageDialogFrame Draw(
        UiRenderContext context,
        IUiFocusState focusScope,
        string title,
        MessageDialogLayout layout,
        ScrollableViewport viewport,
        DialogActionController? actions)
    {
        ScrollableFormFrame? buttons = null;
        IUiCanvas screen = context.Canvas;
        Rect contentBounds = PopupRenderer.GetContentBounds(layout.Bounds, drawBorder: true);
        var textBounds = new Rect(contentBounds.X + 1, contentBounds.Y, Math.Max(1, contentBounds.Width - 2), layout.ContentHeight);
        Rect? scrollbarBounds = layout.MessageLines.Count > layout.ContentHeight
            ? new Rect(layout.Bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height)
            : null;
        ScrollableViewportFrameState viewportFrame = viewport.CalculateFrameState(
            layout.MessageLines.Count, layout.ContentHeight, textBounds, scrollbarBounds);
        ScrollState? scrollState = viewport.GetScrollState(viewportFrame);

        var palette = UiTheme.Current;
        new DialogFrameRenderer().RenderFrame(screen, layout.Bounds, title, false, PaletteStyles.DialogPopupOptions(palette), scrollState, (_, contentBounds) =>
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
                    PaletteStyles.DialogError(palette));
            }

            if (actions is null)
            {
                const string hint = "[ Press Enter ]";
                screen.Write(
                    layout.Bounds.X + Math.Max(0, (layout.Bounds.Width - hint.Length) / 2),
                    layout.ActionRow,
                    hint,
                    PaletteStyles.DialogFill(palette));
                return;
            }

            buttons = actions.Render(
                new FormRenderContext(
                    context,
                    new Rect(textX, layout.ActionRow, textWidth, 1),
                    PaletteStyles.DialogBorder(palette),
                    new Rect(textX, layout.ActionRow, textWidth, 1)),
                focusScope);
        });
        return new MessageDialogFrame(layout, viewportFrame, buttons, actions);
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

    private static UiInputResult RouteViewportInput(
        ConsoleInputEvent input,
        MessageDialogFrame frame,
        UiInputRouteContext route,
        ScrollableViewport viewport) => input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteViewportKey(key, frame, viewport),
            MouseConsoleInputEvent mouse when IsViewportMouseRoute(route) => RouteViewportMouse(mouse, frame, viewport),
            _ => UiInputResult.NotHandled,
        };

    private static UiInputResult RouteViewportKey(
        ConsoleKeyInfo key,
        MessageDialogFrame frame,
        ScrollableViewport viewport) =>
        IsViewportScrollKey(key)
            ? ScrollableViewportRouting.ToUiInputResult(viewport.HandleKey(key, frame.Viewport), ScrollbarTarget)
            : UiInputResult.NotHandled;

    private static UiInputResult RouteViewportMouse(
        MouseConsoleInputEvent mouse,
        MessageDialogFrame frame,
        ScrollableViewport viewport) =>
        ScrollableViewportRouting.ToUiInputResult(viewport.HandleMouse(mouse, frame.Viewport), ScrollbarTarget);

    private static bool IsViewportScrollKey(ConsoleKeyInfo key) =>
        key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.PageUp or
            ConsoleKey.PageDown or ConsoleKey.Home or ConsoleKey.End;

    private static bool IsScrollable(ScrollableViewportFrameState frame) =>
        frame.TotalItems > frame.ViewportItems;

    private static bool IsViewportMouseRoute(UiInputRouteContext route) =>
        route.RouteKind == UiInputRouteKind.HitTarget &&
        (route.Target == ContentTarget || route.Target == ScrollbarTarget) ||
        route.RouteKind == UiInputRouteKind.CapturedTarget && route.Target == ScrollbarTarget;

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
        ScrollableViewportFrameState Viewport,
        ScrollableFormFrame? Buttons,
        DialogActionController? Actions);

    private readonly record struct MessageDialogInput(
        ConsoleInputEvent Input,
        FormInputResult FormResult,
        DialogActionOutcome? ActionOutcome = null);
}
