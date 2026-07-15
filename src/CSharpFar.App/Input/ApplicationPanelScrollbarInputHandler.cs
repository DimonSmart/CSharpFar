using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationPanelScrollbarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationPanelScrollbarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        PanelSide side,
        ApplicationScrollBarFrame? frame,
        UiInputRouteKind routeKind)
    {
        if (frame is null)
            return routeKind == UiInputRouteKind.CapturedTarget
                ? ApplicationInputHandlingResult.FromHandled(shouldRender: false)
                : ApplicationInputHandlingResult.NotHandled;

        var state = _context.GetPanelState(side);

        if (routeKind == UiInputRouteKind.CapturedTarget)
        {
            if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Up)
            {
                _context.Ui.PanelScrollbarDrag = null;
                _context.Mouse.LastLeftPanelItemClick = null;
                return ApplicationInputHandlingResult.FromHandled(shouldRender: false);
            }

            if (input.Kind == MouseEventKind.Move &&
                _context.Ui.PanelScrollbarDrag is { } drag &&
                drag.Side == side)
            {
                int dragFirstVisibleIndex = ScrollBarInteraction.FirstVisibleIndexForThumbY(
                    drag.DragState.Bounds,
                    new ScrollState
                    {
                        TotalItems = drag.DragState.TotalItems,
                        ViewportItems = drag.DragState.ViewportItems,
                        FirstVisibleIndex = state.ScrollOffset,
                    },
                    input.Y,
                    drag.DragState.PointerOffsetInThumb);

                _context.SetActiveSide(side);
                _context.PanelController.ScrollView(
                    state,
                    dragFirstVisibleIndex - state.ScrollOffset,
                    drag.DragState.ViewportItems);
                _context.Mouse.LastLeftPanelItemClick = null;
                return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
            }

            return ApplicationInputHandlingResult.FromHandled(shouldRender: false);
        }

        if (routeKind != UiInputRouteKind.HitTarget ||
            input.Button != MouseButton.Left ||
            input.Kind != MouseEventKind.Down)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        var hit = ScrollBarInteraction.HitTest(frame.Bounds, frame.ToScrollState(), input.X, input.Y);
        if (hit.Part == ScrollBarHitPart.None)
            return ApplicationInputHandlingResult.NotHandled;

        int firstVisibleIndex = frame.FirstVisibleIndex;
        if (hit.Part == ScrollBarHitPart.Thumb)
        {
            _context.Ui.PanelScrollbarDrag = new PanelScrollbarDrag(
                side,
                new ScrollBarDragState(
                    frame.Bounds,
                    frame.TotalItems,
                    frame.ViewportItems,
                    hit.PointerOffsetInThumb));
        }
        else
        {
            _context.Ui.PanelScrollbarDrag = null;
            firstVisibleIndex = ScrollBarInteraction.ApplyClick(frame.ToScrollState(), hit.Part);
        }

        _context.SetActiveSide(side);
        _context.PanelController.ScrollView(state, firstVisibleIndex - state.ScrollOffset, frame.ViewportItems);
        _context.Mouse.LastLeftPanelItemClick = null;
        return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
    }
}
