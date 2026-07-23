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
        ApplicationScrollbarInput? scrollbarInput,
        UiInputRouteKind routeKind)
    {
        if (scrollbarInput is null)
            return routeKind == UiInputRouteKind.CapturedTarget
                ? ApplicationInputHandlingResult.FromHandled(shouldRender: false)
                : ApplicationInputHandlingResult.NotHandled;

        PanelSide side = scrollbarInput.Side;
        VerticalScrollbarInputResult scrollbar = scrollbarInput.Result;
        var state = _context.GetPanelState(side);

        _context.SetActiveSide(side);
        if (scrollbar.PositionChanged)
            _context.PanelController.ScrollView(
                state,
                scrollbar.FirstVisibleIndex - state.ScrollOffset,
                scrollbarInput.ViewportItems);
        _context.Mouse.LastLeftPanelItemClick = null;
        return ApplicationInputHandlingResult.FromHandled(scrollbar.PositionChanged);
    }
}
