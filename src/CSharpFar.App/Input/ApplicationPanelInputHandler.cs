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

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationPanelFrame? frame,
        UiInputRouteKind routeKind)
    {
        if (routeKind != UiInputRouteKind.HitTarget || frame is null)
            return ApplicationInputHandlingResult.NotHandled;

        var state = _context.GetPanelState(frame.Side);

        if (input.Button == MouseButton.Left &&
            input.Kind == MouseEventKind.Down &&
            frame.RetryBounds is { } retryBounds &&
            retryBounds.Contains(input.X, input.Y))
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

        if (input.Button == MouseButton.Right && input.Kind == MouseEventKind.Down)
        {
            _context.Mouse.LastLeftPanelItemClick = null;
            _context.SetActiveSide(frame.Side);
            if (TryGetCurrentItem(input, frame, state, out var hit, out var item))
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
            if (TryGetCurrentItem(input, frame, state, out var hit, out var item))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                var currentClick = new PanelItemClick(frame.Side, hit.ItemIndex, hit.ItemIdentity);
                if (_context.Mouse.LastLeftPanelItemClick == currentClick)
                    _context.OpenPanelItem(state, frame.Side, item);
            }

            _context.Mouse.LastLeftPanelItemClick = null;
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (input.Button == MouseButton.Left && input.Kind == MouseEventKind.Down)
        {
            _context.SetActiveSide(frame.Side);
            if (TryGetCurrentItem(input, frame, state, out var hit, out _))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                _context.Mouse.LastLeftPanelItemClick =
                    new PanelItemClick(frame.Side, hit.ItemIndex, hit.ItemIdentity);
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
        MouseConsoleInputEvent input,
        ApplicationPanelFrame frame,
        FilePanelState state,
        out ApplicationPanelItemHit hit,
        out FilePanelItem item)
    {
        int x = frame.Side == PanelSide.Right && input.X == frame.Bounds.X
            ? input.X + 1
            : input.X;
        hit = frame.VisibleItems.FirstOrDefault(candidate => candidate.Bounds.Contains(x, input.Y))!;
        if (hit is null ||
            hit.ItemIndex < 0 ||
            hit.ItemIndex >= state.Items.Count)
        {
            item = null!;
            return false;
        }

        item = state.Items[hit.ItemIndex];
        return item.FullPath == hit.ItemIdentity;
    }
}
