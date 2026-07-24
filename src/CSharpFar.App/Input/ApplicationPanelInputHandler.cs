using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationPanelInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationPanelInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    // Direct-call adapter kept for focused handler tests. Production input always
    // supplies the semantic target selected by the committed UiInteractionFrame.
    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationPanelFrame frame,
        UiInputRouteKind routeKind)
    {
        UiTargetId? target = ApplicationTargetIds.Panel(frame.Side);
        if (routeKind == UiInputRouteKind.HitTarget)
        {
            var builder = new UiInteractionFrameBuilder()
                .AddHitRegion(ApplicationTargetIds.Panel(frame.Side), frame.Bounds);
            if (frame.RetryBounds is { } retryBounds)
                builder.AddHitRegion(ApplicationTargetIds.PanelRetry(frame.Side), retryBounds);
            foreach (ApplicationPanelItemHit item in frame.VisibleItems)
            {
                UiTargetId itemTarget = ApplicationTargetIds.PanelItem(frame.Side, item.ItemIndex);
                builder.AddHitRegion(itemTarget, item.Bounds);
                if (frame.Side == PanelSide.Right && item.Bounds.X == frame.Bounds.X + 1)
                    builder.AddHitRegion(itemTarget, new Rect(frame.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height));
            }

            if (builder.Build().TryHitTest(input.X, input.Y, out UiHitRegion hit))
                target = hit.Target;
        }

        return Handle(input, frame, routeKind, target);
    }

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationPanelFrame frame,
        UiInputRouteKind routeKind,
        UiTargetId? target)
    {
        if (routeKind != UiInputRouteKind.HitTarget)
            return ApplicationInputHandlingResult.NotHandled;

        var state = _context.GetPanelState(frame.Side);

        if (input.Button == MouseButton.Left &&
            input.Kind == MouseEventKind.Down &&
            frame.IsRetryTarget(target))
        {
            _context.SetActiveSide(frame.Side);
            _context.SafeRefresh(state, frame.VisibleRows);
            _context.Mouse.LastLeftPanelItemClick = null;
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Kind == MouseEventKind.Wheel)
        {
            _context.SetActiveSide(frame.Side);
            int delta = input.Button == MouseButton.WheelUp ? -3 : 3;
            _context.PanelController.ScrollView(state, delta, frame.VisibleRows);
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        bool hasItemTarget = frame.TryGetItemTarget(target, out ApplicationPanelItemHit hit);

        if (input.Button == MouseButton.Right && input.Kind == MouseEventKind.Down)
        {
            _context.Mouse.LastLeftPanelItemClick = null;
            _context.SetActiveSide(frame.Side);
            if (hasItemTarget && TryGetCurrentItem(hit, state, out var item))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                if (_context.PanelOptions().RightClickSelectsFiles &&
                    PanelController.CanSelect(item, _context.PanelOptions()))
                {
                    _context.PanelController.ToggleCurrentSelection(state, _context.PanelOptions());
                }
            }

            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.DoubleClick)
        {
            _context.SetActiveSide(frame.Side);
            if (hasItemTarget && TryGetCurrentItem(hit, state, out var item))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                var currentClick = new PanelItemClick(frame.Side, hit.ItemIndex, hit.ItemLocation);
                if (_context.Mouse.LastLeftPanelItemClick == currentClick)
                    _context.OpenPanelItem(state, frame.Side, item);
            }

            _context.Mouse.LastLeftPanelItemClick = null;
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Down)
        {
            _context.SetActiveSide(frame.Side);
            if (hasItemTarget && TryGetCurrentItem(hit, state, out _))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                _context.Mouse.LastLeftPanelItemClick =
                    new PanelItemClick(frame.Side, hit.ItemIndex, hit.ItemLocation);
            }
            else
            {
                _context.Mouse.LastLeftPanelItemClick = null;
            }

            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        return ApplicationInputHandlingResult.NotHandled;
    }

    private static bool TryGetCurrentItem(
        ApplicationPanelItemHit hit,
        FilePanelState state,
        out FilePanelItem item)
    {
        if (hit.ItemIndex < 0 || hit.ItemIndex >= state.Items.Count)
        {
            item = null!;
            return false;
        }

        item = state.Items[hit.ItemIndex];
        return item.Location == hit.ItemLocation;
    }
}
