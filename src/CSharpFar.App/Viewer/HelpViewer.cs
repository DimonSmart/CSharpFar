using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class HelpViewer
{
    private readonly InteractiveSurfaceHost _surfaces;
    private readonly ConsolePalette _palette;

    public HelpViewer(UiCompositionHost composition, ConsolePalette? palette = null)
    {
        _surfaces = new InteractiveSurfaceHost(composition);
        _palette = palette ?? PaletteRegistry.Default;
    }

    public void Show()
    {
        var layer = new HelpViewerLayer(HelpContent.Lines, _palette);
        _surfaces.Run(layer, static (_, action) => action == HelpAction.Close
            ? ModalDialogLoopResult<bool>.Complete(true)
            : ModalDialogLoopResult<bool>.Continue);
    }
}

internal enum HelpAction { None, Close }

internal sealed class HelpViewerLayer : InteractiveSurfaceLayer<HelpViewerFrame, HelpAction>
{
    internal static readonly UiTargetId Keyboard = new("help.keyboard");
    internal static readonly UiTargetId Content = new("help.content");
    internal static readonly UiTargetId Scrollbar = new("help.vertical-scrollbar");
    internal static readonly UiTargetId FunctionKeys = new("help.function-key-bar");
    private const int KeyColumnWidth = 20;
    private readonly HelpLine[] _lines;
    private readonly ConsolePalette _palette;
    private int _scrollTop;
    private int _scrollLeft;
    private ScrollBarDragState? _drag;

    public HelpViewerLayer(HelpLine[] lines, ConsolePalette palette)
        : base(
            (context, _) => CreateFrame(context, lines, palette, 0, 0),
            frame => BuildInteraction(frame),
            (_, _, _) => new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None))
    {
        _lines = lines;
        _palette = palette;
    }

    protected override HelpViewerFrame RenderFrameCore(UiRenderContext context)
    {
        HelpViewerFrame frame = CreateFrame(context, _lines, _palette, _scrollTop, _scrollLeft);
        Draw(context.Screen, _lines, frame, _palette);
        return frame;
    }

    protected override UiInteractionFrame BuildInteractionFrameCore(HelpViewerFrame frame) => BuildInteraction(frame);

    protected override void OnFrameCommitted(HelpViewerFrame frame)
    {
        _scrollTop = frame.ScrollTop;
        _scrollLeft = frame.ScrollLeft;
        _drag = frame.ScrollBarBounds is { } bar && frame.VerticalScrollState is { } state && _drag is { } drag
            ? ScrollBarInteraction.RebaseDrag(drag, bar, state.TotalItems, state.ViewportItems)
            : null;
    }

    protected override InteractiveSurfaceRouteResult<HelpAction> RouteSemanticInput(ConsoleInputEvent input, HelpViewerFrame frame, UiInputRouteContext context)
    {
        if (input is KeyConsoleInputEvent key && context.RouteKind == UiInputRouteKind.KeyboardTarget && context.Target == Keyboard)
        {
            var (action, invalidate) = RouteKey(key.Key.Key, frame);
            return new InteractiveSurfaceRouteResult<HelpAction>(action, invalidate);
        }

        if (input is MouseConsoleInputEvent mouse)
            return RouteMouse(mouse, frame, context);

        return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None);
    }

    private (HelpAction Action, bool Invalidate) RouteKey(ConsoleKey key, HelpViewerFrame frame)
    {
        int oldTop = _scrollTop;
        int oldLeft = _scrollLeft;
        switch (key)
        {
            case ConsoleKey.UpArrow: _scrollTop = Math.Max(0, frame.ScrollTop - 1); break;
            case ConsoleKey.DownArrow: _scrollTop = Math.Min(frame.MaxScrollTop, frame.ScrollTop + 1); break;
            case ConsoleKey.LeftArrow: _scrollLeft = Math.Max(0, frame.ScrollLeft - 1); break;
            case ConsoleKey.RightArrow: _scrollLeft = Math.Min(frame.MaxScrollLeft, frame.ScrollLeft + 1); break;
            case ConsoleKey.PageUp: _scrollTop = Math.Max(0, frame.ScrollTop - frame.VisibleRows); break;
            case ConsoleKey.PageDown: _scrollTop = Math.Min(frame.MaxScrollTop, frame.ScrollTop + frame.VisibleRows); break;
            case ConsoleKey.Home: _scrollTop = _scrollLeft = 0; break;
            case ConsoleKey.End: _scrollTop = frame.MaxScrollTop; _scrollLeft = 0; break;
            case ConsoleKey.F1:
            case ConsoleKey.F10:
            case ConsoleKey.Escape: return (HelpAction.Close, false);
            default: return (HelpAction.None, false);
        }

        return (HelpAction.None, oldTop != _scrollTop || oldLeft != _scrollLeft);
    }

    private InteractiveSurfaceRouteResult<HelpAction> RouteMouse(MouseConsoleInputEvent mouse, HelpViewerFrame frame, UiInputRouteContext context)
    {
        if (context.Target == FunctionKeys && mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down })
        {
            HelpFooterActionHit? hit = frame.FooterActionHits.FirstOrDefault(value => value.Bounds.Contains(mouse.X, mouse.Y));
            return new InteractiveSurfaceRouteResult<HelpAction>(hit?.Action ?? HelpAction.None);
        }

        if (context.Target == Content && mouse.Kind == MouseEventKind.Wheel)
        {
            int oldTop = _scrollTop;
            if (mouse.Button == MouseButton.WheelUp)
                _scrollTop = Math.Max(0, frame.ScrollTop - 3);
            else if (mouse.Button == MouseButton.WheelDown)
                _scrollTop = Math.Min(frame.MaxScrollTop, frame.ScrollTop + 3);

            return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None, oldTop != _scrollTop);
        }

        if (frame.ScrollBarBounds is not { } bar || frame.VerticalScrollState is not { } state || context.Target != Scrollbar)
            return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None);

        if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Up } && _drag is not null)
        {
            _drag = null;
            return new InteractiveSurfaceRouteResult<HelpAction>(
                HelpAction.None,
                MouseCaptureRequest: UiMouseCaptureRequest.Release);
        }

        if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Move } && _drag is { } drag)
        {
            int oldTop = _scrollTop;
            _scrollTop = ScrollBarInteraction.FirstVisibleIndexForThumbY(bar, state, mouse.Y, drag.PointerOffsetInThumb);
            return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None, oldTop != _scrollTop);
        }

        if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None);

        ScrollBarHitTestResult scrollbarHit = ScrollBarInteraction.HitTest(bar, state, mouse.X, mouse.Y);
        if (scrollbarHit.Part == ScrollBarHitPart.Thumb)
        {
            _drag = new ScrollBarDragState(bar, state.TotalItems, state.ViewportItems, scrollbarHit.PointerOffsetInThumb);
            return new InteractiveSurfaceRouteResult<HelpAction>(
                HelpAction.None,
                MouseCaptureRequest: UiMouseCaptureRequest.Capture(Scrollbar, MouseButton.Left));
        }

        int previousTop = _scrollTop;
        _scrollTop = ScrollBarInteraction.ApplyClick(state, scrollbarHit.Part);
        return new InteractiveSurfaceRouteResult<HelpAction>(HelpAction.None, previousTop != _scrollTop);
    }

    private static HelpViewerFrame CreateFrame(UiRenderContext context, HelpLine[] lines, ConsolePalette palette, int scrollTop, int scrollLeft)
    {
        int width = context.Size.Width;
        int height = context.Size.Height;
        int visibleRows = Math.Max(0, height - 2);
        int maxTop = Math.Max(0, lines.Length - visibleRows);
        int top = Math.Clamp(scrollTop, 0, maxTop);
        bool scrollbarVisible = visibleRows > 0 && lines.Length > visibleRows && width > 1;
        Rect content = new(0, 1, Math.Max(0, width - (scrollbarVisible ? 1 : 0)), visibleRows);
        int maxLeft = Math.Max(0, MaximumDisplayWidth(lines) - content.Width);
        int left = Math.Clamp(scrollLeft, 0, maxLeft);
        Rect? scrollbar = scrollbarVisible ? new Rect(width - 1, 1, 1, visibleRows) : null;
        ScrollState? scrollState = scrollbar is { } bar
            ? new ScrollState { TotalItems = lines.Length, ViewportItems = visibleRows, FirstVisibleIndex = top }
            : null;
        Rect footer = height > 0 ? new Rect(0, height - 1, width, 1) : new Rect(0, 0, 0, 0);
        IReadOnlyList<HelpFooterActionHit> footerHits = BuildFooterActionHits(footer);

        return new HelpViewerFrame(
            context.Viewport,
            new Rect(0, 0, width, Math.Min(1, height)),
            content,
            footerHits,
            top,
            left,
            maxTop,
            maxLeft,
            visibleRows,
            scrollbar,
            scrollState);
    }

    private static IReadOnlyList<HelpFooterActionHit> BuildFooterActionHits(Rect footer) =>
        footer.Width >= 7 && footer.Height > 0
            ? Array.AsReadOnly([new HelpFooterActionHit(new Rect(footer.X, footer.Y, 7, 1), HelpAction.Close, "F10")])
            : Array.Empty<HelpFooterActionHit>();

    private static UiInteractionFrame BuildInteraction(HelpViewerFrame frame)
    {
        var builder = new UiInteractionFrameBuilder()
            .AddFocusEntry(Keyboard, 0, cursor: new UiCursorPlacement(0, 0, false))
            .SetDefaultFocusTarget(Keyboard)
            .SetKeyboardTarget(Keyboard);
        if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0)
            builder.AddHitRegion(Content, frame.ContentBounds);
        if (frame.ScrollBarBounds is { } bar &&
            frame.VerticalScrollState is { } scrollState &&
            ScrollBarInteraction.IsInteractive(bar, scrollState))
        {
            builder.AddHitRegion(Scrollbar, bar);
        }
        foreach (HelpFooterActionHit action in frame.FooterActionHits)
            builder.AddHitRegion(FunctionKeys, action.Bounds);

        return builder.Build();
    }

    private static void Draw(ScreenRenderer screen, HelpLine[] lines, HelpViewerFrame frame, ConsolePalette palette)
    {
        int width = frame.Viewport.Size.Width;
        int height = frame.Viewport.Size.Height;
        if (width <= 0 || height <= 0)
            return;

        string pos = lines.Length == 0 ? " 0/0 " : $" {frame.ScrollTop + 1}/{lines.Length} ";
        int nameWidth = Math.Max(0, width - pos.Length);
        screen.Write(0, 0, (" CSharpFar Help ".PadRight(nameWidth)[..nameWidth] + pos)[..width], PaletteStyles.PathHeaderActive(palette));
        CellStyle body = PaletteStyles.HelpBody(palette);
        for (int row = 0; row < frame.VisibleRows; row++)
        {
            int line = frame.ScrollTop + row;
            screen.FillRegion(new Rect(0, row + 1, width, 1), body);
            if (line < lines.Length)
                DrawLine(screen, lines[line], row + 1, frame.ScrollLeft, frame.ContentBounds.Width, palette);
        }

        if (frame.ScrollBarBounds is { } scrollbar && frame.VerticalScrollState is { } scrollState)
            new ScrollBarRenderer().RenderVerticalScrollbar(screen, scrollbar, scrollState, new ScrollBarOptions { Enabled = true }, body);
        screen.FillRegion(new Rect(0, height - 1, width, 1), PaletteStyles.KeyBarLabel(palette));
        screen.Write(0, height - 1, "10", PaletteStyles.KeyBarNum(palette));
        screen.Write(2, height - 1, "Close", PaletteStyles.KeyBarLabel(palette));
    }

    private static void DrawLine(ScreenRenderer screen, HelpLine line, int y, int left, int width, ConsolePalette palette)
    {
        if (width <= 0)
            return;

        if (line.Kind == HelpLineKind.KeyLine)
        {
            WriteClipped(screen, 0, y, $"  {line.Key}".PadRight(KeyColumnWidth), 0, left, width, PaletteStyles.HelpKey(palette));
            WriteClipped(screen, 0, y, line.Description, KeyColumnWidth, left, width, PaletteStyles.HelpBody(palette));
            return;
        }

        CellStyle style = line.Kind switch
        {
            HelpLineKind.Title or HelpLineKind.Heading => PaletteStyles.HelpHeading(palette),
            HelpLineKind.Separator => PaletteStyles.HelpSeparator(palette),
            _ => PaletteStyles.HelpBody(palette),
        };
        WriteClipped(screen, 0, y, line.Description, 0, left, width, style);
    }

    private static void WriteClipped(
        ScreenRenderer screen,
        int x,
        int y,
        string text,
        int textStart,
        int left,
        int width,
        CellStyle style)
    {
        int visibleStart = Math.Max(textStart, left);
        int visibleEnd = Math.Min(textStart + text.Length, left + width);
        if (visibleStart >= visibleEnd)
            return;

        int sourceStart = visibleStart - textStart;
        int count = visibleEnd - visibleStart;
        screen.Write(x + visibleStart - left, y, text.Substring(sourceStart, count), style);
    }

    private static int MaximumDisplayWidth(IEnumerable<HelpLine> lines) =>
        lines.Select(DisplayWidth).DefaultIfEmpty(0).Max();

    private static int DisplayWidth(HelpLine line) =>
        line.Kind == HelpLineKind.KeyLine
            ? KeyColumnWidth + line.Description.Length
            : line.Description.Length;
}

internal sealed record HelpFooterActionHit(Rect Bounds, HelpAction Action, string Key);

internal sealed record HelpViewerFrame(
    ConsoleViewport Viewport,
    Rect HeaderBounds,
    Rect ContentBounds,
    IReadOnlyList<HelpFooterActionHit> FooterActionHits,
    int ScrollTop,
    int ScrollLeft,
    int MaxScrollTop,
    int MaxScrollLeft,
    int VisibleRows,
    Rect? ScrollBarBounds,
    ScrollState? VerticalScrollState);
