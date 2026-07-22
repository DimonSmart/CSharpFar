using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ModuleHelpDialog
{
    private static readonly UiTargetId HelpTarget = new("module-help");
    private static readonly UiTargetId ContentTarget = new("module-help-content");
    private static readonly UiTargetId ScrollbarTarget = new("module-help-scrollbar");
    private readonly ModalDialogHost _modalDialogs;

    public ModuleHelpDialog(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public void Show(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var viewport = new ScrollableViewport();
        _modalDialogs.RunInteractive<ModuleHelpFrame, ConsoleInputEvent, Unit>(
            (context, _) =>
            {
                ModuleHelpFrame frame = CalculateFrame(context.Size, lines.Count, viewport);
                Draw(context.Canvas, title, lines, viewport, frame);
                return frame;
            },
            BuildInteractionFrame,
            (input, frame, route) => (input, RouteInput(input, frame, route, viewport)),
            (routed, input) =>
            {
                if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F1 or ConsoleKey.F10 })
                    return ModalDialogLoopResult<Unit>.Complete(default);
                return ModalDialogLoopResult<Unit>.Continue;
            },
            applyCommittedFrame: frame => viewport.ApplyCommittedFrame(frame.Viewport));
    }

    private static ModuleHelpFrame CalculateFrame(ConsoleSize size, int lineCount, ScrollableViewport viewport)
    {
        int contentHeight = Math.Max(1, size.Height - 2);
        var contentBounds = new Rect(0, 1, Math.Max(0, size.Width - 1), contentHeight);
        Rect? scrollbarBounds = lineCount > contentHeight
            ? new Rect(Math.Max(0, size.Width - 1), 1, 1, contentHeight)
            : null;
        return new ModuleHelpFrame(size, viewport.CalculateFrameState(lineCount, contentHeight, contentBounds, scrollbarBounds));
    }

    private static UiInteractionFrame BuildInteractionFrame(ModuleHelpFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddHitRegion(ContentTarget, frame.Viewport.ContentBounds)
            .AddFocusEntry(HelpTarget, 0, cursor: new UiCursorPlacement(0, 0, Visible: false))
            .SetDefaultFocusTarget(HelpTarget)
            .SetKeyboardTarget(HelpTarget);
        if (frame.Viewport.ScrollbarBounds is Rect scrollbar)
            builder.AddHitRegion(ScrollbarTarget, scrollbar);
        return builder.Build();
    }

    private static UiInputResult RouteInput(
        ConsoleInputEvent input,
        ModuleHelpFrame frame,
        UiInputRouteContext route,
        ScrollableViewport viewport)
    {
        ScrollableViewportInputResult result = input switch
        {
            KeyConsoleInputEvent key => viewport.HandleKey(key.Key, frame.Viewport),
            MouseConsoleInputEvent mouse when IsViewportMouseRoute(route) => viewport.HandleMouse(mouse, frame.Viewport),
            _ => ScrollableViewportInputResult.NotHandled,
        };
        return ScrollableViewportRouting.ToUiInputResult(result, ScrollbarTarget);
    }

    private static bool IsViewportMouseRoute(UiInputRouteContext route) =>
        route.RouteKind == UiInputRouteKind.HitTarget &&
        (route.Target == ContentTarget || route.Target == ScrollbarTarget) ||
        route.RouteKind == UiInputRouteKind.CapturedTarget && route.Target == ScrollbarTarget;

    private static void Draw(IUiCanvas screen, string title, IReadOnlyList<string> lines, ScrollableViewport viewport, ModuleHelpFrame frame)
    {
        var palette = UiTheme.Current;
        var headerStyle = PaletteStyles.PathHeaderActive(palette);
        string position = lines.Count == 0 ? " 0/0 " : $" {frame.Viewport.FirstVisibleIndex + 1}/{lines.Count} ";
        int titleWidth = Math.Max(0, frame.Size.Width - position.Length);
        screen.Write(0, 0, Truncate(" " + title + " ", titleWidth).PadRight(titleWidth) + position, headerStyle);

        var bodyStyle = PaletteStyles.HelpBody(palette);
        for (int row = 0; row < frame.Viewport.ViewportItems; row++)
        {
            int lineIndex = frame.Viewport.FirstVisibleIndex + row;
            string text = lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
            screen.Write(0, row + 1, Truncate(text, frame.Viewport.ContentBounds.Width).PadRight(frame.Viewport.ContentBounds.Width), bodyStyle);
        }

        if (frame.Viewport.ScrollbarBounds is Rect scrollbarBounds && viewport.GetScrollState(frame.Viewport) is { } scrollState)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(screen, scrollbarBounds, scrollState,
                new ScrollBarOptions { Enabled = true, DrawWhenNotScrollable = false }, PaletteStyles.DialogBorder(palette));
        }

        screen.Write(0, frame.Size.Height - 1, "Esc/F10 Close".PadRight(frame.Size.Width), PaletteStyles.KeyBarLabel(palette));
    }

    private static string Truncate(string value, int width) =>
        width <= 0 ? string.Empty : value.Length <= width ? value : value[..width];

    private readonly record struct ModuleHelpFrame(ConsoleSize Size, ScrollableViewportFrameState Viewport);
}
