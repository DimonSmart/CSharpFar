using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class HelpViewer
{
    private const int KeyColumnWidth = 20;
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

    private enum HelpAction { None, Close }

    private sealed class HelpViewerLayer : InteractiveSurfaceLayer<HelpViewerFrame, HelpAction>
    {
        private static readonly UiTargetId Keyboard = new("help.keyboard");
        private static readonly UiTargetId Content = new("help.content");
        private static readonly UiTargetId Scrollbar = new("help.vertical-scrollbar");
        private static readonly UiTargetId FunctionKeys = new("help.function-key-bar");
        private readonly HelpLine[] _lines;
        private readonly ConsolePalette _palette;
        private int _scrollTop;
        private int _scrollLeft;
        private ScrollBarDragState? _drag;

        public HelpViewerLayer(HelpLine[] lines, ConsolePalette palette)
            : base(
                (context, _) => CreateFrame(context, lines, palette, 0, 0),
                frame => BuildInteraction(frame),
                (_, _, _) => (HelpAction.None, UiInputResult.NotHandled))
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
            _drag = frame.ScrollBar is { } bar && _drag is { } drag
                ? ScrollBarInteraction.RebaseDrag(drag, bar, _lines.Length, frame.VisibleRows)
                : null;
        }

        protected override (HelpAction Semantic, UiInputResult UiResult) RouteSemanticInput(ConsoleInputEvent input, HelpViewerFrame frame, UiInputRouteContext context)
        {
            HelpAction action = HelpAction.None;
            UiInputResult result = UiInputResult.NotHandled;
            if (input is KeyConsoleInputEvent key && context.RouteKind == UiInputRouteKind.KeyboardTarget && context.Target == Keyboard)
            {
                action = RouteKey(key.Key.Key, frame);
                result = action == HelpAction.Close ? UiInputResult.HandledResult : UiInputResult.HandledAndInvalidate;
            }
            else if (input is MouseConsoleInputEvent mouse)
            {
                result = RouteMouse(mouse, frame, context, ref action);
            }

            // The base layer owns the packet; use its normal routing contract.
            return (action, result);
        }

        private HelpAction RouteKey(ConsoleKey key, HelpViewerFrame frame)
        {
            switch (key)
            {
                case ConsoleKey.UpArrow: _scrollTop = Math.Max(0, frame.ScrollTop - 1); break;
                case ConsoleKey.DownArrow: _scrollTop = Math.Min(frame.MaxScrollTop, frame.ScrollTop + 1); break;
                case ConsoleKey.LeftArrow: _scrollLeft = Math.Max(0, frame.ScrollLeft - 1); break;
                case ConsoleKey.RightArrow: _scrollLeft = frame.ScrollLeft + 1; break;
                case ConsoleKey.PageUp: _scrollTop = Math.Max(0, frame.ScrollTop - frame.VisibleRows); break;
                case ConsoleKey.PageDown: _scrollTop = Math.Min(frame.MaxScrollTop, frame.ScrollTop + frame.VisibleRows); break;
                case ConsoleKey.Home: _scrollTop = _scrollLeft = 0; break;
                case ConsoleKey.End: _scrollTop = frame.MaxScrollTop; _scrollLeft = 0; break;
                case ConsoleKey.F1:
                case ConsoleKey.F10:
                case ConsoleKey.Escape: return HelpAction.Close;
                default: return HelpAction.None;
            }
            return HelpAction.None;
        }

        private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, HelpViewerFrame frame, UiInputRouteContext context, ref HelpAction action)
        {
            if (context.Target == FunctionKeys && mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            {
                action = HelpAction.Close;
                return UiInputResult.HandledResult;
            }
            if (context.Target == Content && mouse.Kind == MouseEventKind.Wheel)
            {
                _scrollTop = mouse.Button == MouseButton.WheelUp
                    ? Math.Max(0, frame.ScrollTop - 3)
                    : Math.Min(frame.MaxScrollTop, frame.ScrollTop + 3);
                return UiInputResult.HandledAndInvalidate;
            }
            if (frame.ScrollBar is not { } bar || context.Target != Scrollbar)
                return UiInputResult.NotHandled;

            ScrollState state = new() { TotalItems = _lines.Length, ViewportItems = frame.VisibleRows, FirstVisibleIndex = frame.ScrollTop };
            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Up } && _drag is not null)
            {
                _drag = null;
                return UiInputResult.ReleaseMouse();
            }
            if (mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Move } && _drag is { } drag)
            {
                _scrollTop = ScrollBarInteraction.FirstVisibleIndexForThumbY(bar, state, mouse.Y, drag.PointerOffsetInThumb);
                return UiInputResult.HandledAndInvalidate;
            }
            if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down }) return UiInputResult.NotHandled;
            ScrollBarHitTestResult hit = ScrollBarInteraction.HitTest(bar, state, mouse.X, mouse.Y);
            if (hit.Part == ScrollBarHitPart.Thumb)
            {
                _drag = new ScrollBarDragState(bar, _lines.Length, frame.VisibleRows, hit.PointerOffsetInThumb);
                return UiInputResult.CaptureMouse(Scrollbar, MouseButton.Left, invalidate: false);
            }
            _scrollTop = ScrollBarInteraction.ApplyClick(state, hit.Part);
            return UiInputResult.HandledAndInvalidate;
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
            Rect? scrollbar = scrollbarVisible ? new Rect(width - 1, 1, 1, visibleRows) : null;
            Rect footer = height > 0 ? new Rect(0, height - 1, Math.Min(width, 7), 1) : new Rect(0, 0, 0, 0);
            return new HelpViewerFrame(context.Viewport, new Rect(0, 0, width, Math.Min(1, height)), content, footer,
                top, Math.Max(0, scrollLeft), maxTop, visibleRows, scrollbar);
        }

        private static UiInteractionFrame BuildInteraction(HelpViewerFrame frame)
        {
            var regions = new List<UiHitRegion>();
            if (frame.ContentBounds.Width > 0 && frame.ContentBounds.Height > 0) regions.Add(new(Content, frame.ContentBounds));
            if (frame.ScrollBar is { } bar && bar.Height >= 3) regions.Add(new(Scrollbar, bar));
            if (frame.FooterBounds.Width >= 7 && frame.FooterBounds.Height > 0) regions.Add(new(FunctionKeys, frame.FooterBounds));
            return new UiInteractionFrame(regions, new UiFocusFrame([new UiFocusEntry(Keyboard, 0, Cursor: new UiCursorPlacement(0, 0, false))], Keyboard), Keyboard);
        }

        private static void Draw(ScreenRenderer screen, HelpLine[] lines, HelpViewerFrame frame, ConsolePalette palette)
        {
            int width = frame.Viewport.Size.Width;
            int height = frame.Viewport.Size.Height;
            if (width <= 0 || height <= 0) return;
            string pos = lines.Length == 0 ? " 0/0 " : $" {frame.ScrollTop + 1}/{lines.Length} ";
            int nameWidth = Math.Max(0, width - pos.Length);
            screen.Write(0, 0, (" CSharpFar Help ".PadRight(nameWidth)[..nameWidth] + pos)[..width], PaletteStyles.PathHeaderActive(palette));
            CellStyle body = PaletteStyles.HelpBody(palette);
            for (int row = 0; row < frame.VisibleRows; row++)
            {
                int line = frame.ScrollTop + row;
                screen.FillRegion(new Rect(0, row + 1, width, 1), body);
                if (line < lines.Length) DrawLine(screen, lines[line], row + 1, frame.ScrollLeft, frame.ContentBounds.Width, palette);
            }
            if (frame.ScrollBar is { } scrollbar)
                new ScrollBarRenderer().RenderVerticalScrollbar(screen, scrollbar, new ScrollState { TotalItems = lines.Length, ViewportItems = frame.VisibleRows, FirstVisibleIndex = frame.ScrollTop }, new ScrollBarOptions { Enabled = true }, body);
            screen.FillRegion(new Rect(0, height - 1, width, 1), PaletteStyles.KeyBarLabel(palette));
            screen.Write(0, height - 1, "10", PaletteStyles.KeyBarNum(palette));
            screen.Write(2, height - 1, "Close", PaletteStyles.KeyBarLabel(palette));
        }

        private static void DrawLine(ScreenRenderer screen, HelpLine line, int y, int left, int width, ConsolePalette palette)
        {
            CellStyle style = line.Kind switch { HelpLineKind.Title or HelpLineKind.Heading => PaletteStyles.HelpHeading(palette), HelpLineKind.Separator => PaletteStyles.HelpSeparator(palette), HelpLineKind.KeyLine => PaletteStyles.HelpKey(palette), _ => PaletteStyles.HelpBody(palette) };
            string text = line.Kind == HelpLineKind.KeyLine ? $"  {line.Key}".PadRight(KeyColumnWidth + 2) + line.Description : line.Description;
            if (width > 0 && left < text.Length) screen.Write(0, y, text[left..Math.Min(text.Length, left + width)], style);
        }
    }

    private sealed record HelpViewerFrame(ConsoleViewport Viewport, Rect HeaderBounds, Rect ContentBounds, Rect FooterBounds, int ScrollTop, int ScrollLeft, int MaxScrollTop, int VisibleRows, Rect? ScrollBar);
}
