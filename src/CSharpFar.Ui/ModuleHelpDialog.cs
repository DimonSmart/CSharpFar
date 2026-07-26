using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ModuleHelpDialog
{
    private static readonly UiTargetScope Targets = new("module-help");
    private static readonly UiTargetId HelpTarget = Targets.Root;
    private static readonly UiTargetId ContentTarget = Targets.Child("content");
    private static readonly UiTargetId ScrollbarTarget = Targets.Child("scrollbar");
    private readonly ModalDialogHost _modalDialogs;

    public ModuleHelpDialog(ModalDialogHost modalDialogs) =>
        _modalDialogs = modalDialogs ?? throw new ArgumentNullException(nameof(modalDialogs));

    public void Show(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var viewport = new RoutedScrollableViewport(ContentTarget, ScrollbarTarget);
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

    private static ModuleHelpFrame CalculateFrame(ConsoleSize size, int lineCount, RoutedScrollableViewport viewport)
    {
        int contentHeight = Math.Max(1, size.Height - 2);
        var contentBounds = new Rect(0, 1, Math.Max(0, size.Width - 1), contentHeight);
        Rect? scrollbarBounds = lineCount > contentHeight
            ? new Rect(Math.Max(0, size.Width - 1), 1, 1, contentHeight)
            : null;
        return new ModuleHelpFrame(size, viewport.CalculateFrame(lineCount, contentHeight, contentBounds, scrollbarBounds), viewport);
    }

    private static UiInteractionFrame BuildInteractionFrame(ModuleHelpFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFragment(frame.ViewportControl.BuildInteractionFragment(frame.Viewport))
            .AddFocusEntry(HelpTarget, 0, cursor: new UiCursorPlacement(0, 0, Visible: false))
            .SetDefaultFocusTarget(HelpTarget)
            .SetKeyboardTarget(HelpTarget);
        return builder.Build();
    }

    private static UiInputResult RouteInput(
        ConsoleInputEvent input,
        ModuleHelpFrame frame,
        UiInputRouteContext route,
        RoutedScrollableViewport viewport)
    {
        return viewport.RouteInput(input, frame.Viewport, route).UiResult;
    }

    private static void Draw(IUiCanvas screen, string title, IReadOnlyList<string> lines, RoutedScrollableViewport viewport, ModuleHelpFrame frame)
    {
        var palette = UiTheme.Current;
        var headerStyle = PaletteStyles.PathHeaderActive(palette);
        string position = lines.Count == 0 ? " 0/0 " : $" {frame.Viewport.FirstVisibleIndex + 1}/{lines.Count} ";
        int titleWidth = Math.Max(0, frame.Size.Width - ConsoleTextMetrics.GetCellWidth(position));
        screen.Write(0, 0, ConsoleTextMetrics.FitToCells(" " + title + " ", titleWidth) + position, headerStyle);

        var bodyStyle = PaletteStyles.HelpBody(palette);
        for (int row = 0; row < frame.Viewport.ViewportItems; row++)
        {
            int lineIndex = frame.Viewport.FirstVisibleIndex + row;
            string text = lineIndex < lines.Count ? lines[lineIndex] : string.Empty;
            screen.Write(0, row + 1, ConsoleTextMetrics.FitToCells(text, frame.Viewport.ContentBounds.Width), bodyStyle);
        }

        viewport.RenderScrollbar(screen, frame.Viewport, PaletteStyles.DialogBorder(palette));

        screen.Write(0, frame.Size.Height - 1, ConsoleTextMetrics.FitToCells("Esc/F10 Close", frame.Size.Width), PaletteStyles.KeyBarLabel(palette));
    }

    private static string Truncate(string value, int width) =>
        ConsoleTextMetrics.TruncateToCells(value, width);

    private readonly record struct ModuleHelpFrame(
        ConsoleSize Size,
        ScrollableViewportFrameState Viewport,
        RoutedScrollableViewport ViewportControl);
}
