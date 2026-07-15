using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.HitTesting;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class MouseInputRouter
{
    private readonly MouseInputContext _context;

    public MouseInputRouter(MouseInputContext context)
    {
        _context = context;
    }

    public bool Handle(MouseConsoleInputEvent evt, ApplicationUiFrame frame)
    {
        if (TryHandleFunctionKeyBarMouse(evt, frame))
            return true;

        if (frame.Mode != ApplicationSurfaceMode.Panels)
            return false;

        if (TryHandleDirectoryShortcutBarMouse(evt, frame))
            return true;

        if (TryHandlePanelScrollbarDrag(evt))
            return true;

        bool inLeft = _context.IsPanelVisible(PanelSide.Left) &&
            frame.LeftPanelBounds is { } leftBounds &&
            leftBounds.Contains(evt.X, evt.Y);
        bool inRight = _context.IsPanelVisible(PanelSide.Right) &&
            frame.RightPanelBounds is { } rightBounds &&
            rightBounds.Contains(evt.X, evt.Y);
        if (!inLeft && !inRight)
        {
            ClearPanelItemClickOnMousePress(evt);
            return false;
        }

        var side = inLeft ? PanelSide.Left : PanelSide.Right;
        var state = _context.GetPanelState(side);
        var mode = _context.ViewModeForSide(side);
        var bounds = inLeft ? frame.LeftPanelBounds!.Value : frame.RightPanelBounds!.Value;
        int visibleRows = _context.VisibleRowsForSide(side);

        if (_context.QuickView() && side != _context.ActiveSide())
        {
            ClearPanelItemClickOnMousePress(evt);
            return false;
        }

        if (TryHandlePanelScrollbarMouse(evt, side, state, mode, bounds, visibleRows))
            return true;

        if (evt.Button == MouseButton.Left &&
            evt.Kind == MouseEventKind.Down &&
            PanelErrorRenderer.HitTestRetry(evt.X, evt.Y, bounds, state, mode, _context.PanelOptions()))
        {
            _context.SetActiveSide(side);
            _context.SafeRefresh(state, visibleRows);
            _context.Mouse.LastLeftPanelItemClick = null;
            return true;
        }

        if (evt.Kind == MouseEventKind.Wheel)
        {
            _context.SetActiveSide(side);
            int delta = evt.Button == MouseButton.WheelUp ? -3 : 3;
            _context.PanelController.ScrollView(state, delta, visibleRows);
            return true;
        }

        if (evt.Button == MouseButton.Right && evt.Kind == MouseEventKind.Down)
        {
            _context.Mouse.LastLeftPanelItemClick = null;
            _context.SetActiveSide(side);
            int? itemIndex = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIndex.HasValue)
            {
                _context.PanelController.SetCursorTo(state, itemIndex.Value, visibleRows);
                if (_context.PanelOptions().RightClickSelectsFiles)
                {
                    var item = state.Items[itemIndex.Value];
                    if (PanelController.CanSelect(item, _context.PanelOptions()))
                        _context.PanelController.ToggleCurrentSelection(state, _context.PanelOptions());
                }
            }

            return true;
        }

        if (evt.Button == MouseButton.Left && evt.Kind == MouseEventKind.DoubleClick)
        {
            _context.SetActiveSide(side);
            int? itemIndex = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIndex.HasValue)
            {
                _context.PanelController.SetCursorTo(state, itemIndex.Value, visibleRows);

                var item = state.Items[itemIndex.Value];
                var currentClick = new PanelItemClick(side, itemIndex.Value, item.FullPath);
                if (_context.Mouse.LastLeftPanelItemClick == currentClick)
                    _context.OpenPanelItem(state, side, item);
            }

            _context.Mouse.LastLeftPanelItemClick = null;
            return true;
        }

        if (evt.Button == MouseButton.Left &&
            evt.Kind == MouseEventKind.Down)
        {
            _context.SetActiveSide(side);
            int? itemIndex = HitTestPanelItemForMouse(evt, side, bounds, state, mode);
            if (itemIndex.HasValue)
            {
                _context.PanelController.SetCursorTo(state, itemIndex.Value, visibleRows);
                var item = state.Items[itemIndex.Value];
                _context.Mouse.LastLeftPanelItemClick =
                    new PanelItemClick(side, itemIndex.Value, item.FullPath);
            }
            else
            {
                _context.Mouse.LastLeftPanelItemClick = null;
            }

            return true;
        }

        return false;
    }

    private bool TryHandleFunctionKeyBarMouse(MouseConsoleInputEvent evt, ApplicationUiFrame frame)
    {
        var size = frame.Viewport.Size;
        var actions = _context.FunctionKeyBindings
            .Where(binding => binding.Layer == _context.FunctionKeyLayer())
            .Select(binding => new FunctionKeyBarAction<string>(
                binding.KeyNumber,
                binding.Label,
                binding.CommandId,
                _context.CanExecuteFunctionKeyCommand(binding.CommandId)))
            .ToArray();

        if (!new FunctionKeyBarController<string>().TryGetAction(
                evt,
                size.Height - 1,
                size.Width,
                actions,
                out string commandId))
        {
            return false;
        }

        return _context.ExecuteRegisteredCommand(commandId, null);
    }

    private bool TryHandleDirectoryShortcutBarMouse(MouseConsoleInputEvent evt, ApplicationUiFrame frame)
    {
        var size = frame.Viewport.Size;
        if (!DirectoryShortcutBarRenderer.TryGetShortcutNumberAt(
                evt,
                ApplicationLayoutService.PanelHeight(size) - 1,
                size.Width,
                _context.DirectoryShortcuts(),
                out int number))
        {
            return false;
        }

        return _context.ExecuteRegisteredCommand(
            DirectoryShortcutCommandIds.Navigate,
            new NavigateToDirectoryShortcutArgs(number));
    }

    private int? HitTestPanelItemForMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        Rect bounds,
        FilePanelState state,
        PanelViewMode mode)
    {
        int x = evt.X;

        // Accept the right-panel left border as the first item column.
        if (side == PanelSide.Right && x == bounds.X)
            x++;

        return PanelHitTester.HitTestItem(x, evt.Y, bounds, state, mode, _context.PanelOptions());
    }

    private void ClearPanelItemClickOnMousePress(MouseConsoleInputEvent evt)
    {
        if (evt.Kind is MouseEventKind.Down or MouseEventKind.DoubleClick)
            _context.Mouse.LastLeftPanelItemClick = null;
    }

    private bool TryHandlePanelScrollbarDrag(MouseConsoleInputEvent evt)
    {
        if (_context.Ui.PanelScrollbarDrag is not { } drag)
            return false;

        var state = _context.GetPanelState(drag.Side);
        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = drag.DragState;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                drag.DragState.Bounds,
                drag.DragState.TotalItems,
                drag.DragState.ViewportItems,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _context.Ui.PanelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(drag.Side, dragState.Value)
            : null;

        _context.SetActiveSide(drag.Side);
        _context.PanelController.ScrollView(
            state,
            firstVisibleIndex - state.ScrollOffset,
            drag.DragState.ViewportItems);
        _context.Mouse.LastLeftPanelItemClick = null;
        return true;
    }

    private bool TryHandlePanelScrollbarMouse(
        MouseConsoleInputEvent evt,
        PanelSide side,
        FilePanelState state,
        PanelViewMode mode,
        Rect bounds,
        int visibleRows)
    {
        if (!TryGetPanelScrollbarBounds(bounds, mode, out var scrollbarBounds))
            return false;

        int firstVisibleIndex = state.ScrollOffset;
        ScrollBarDragState? dragState = null;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                evt,
                scrollbarBounds,
                state.Items.Count,
                visibleRows,
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        _context.Ui.PanelScrollbarDrag = dragState.HasValue
            ? new PanelScrollbarDrag(side, dragState.Value)
            : null;

        _context.SetActiveSide(side);
        _context.PanelController.ScrollView(state, firstVisibleIndex - state.ScrollOffset, visibleRows);
        _context.Mouse.LastLeftPanelItemClick = null;
        return true;
    }

    private bool TryGetPanelScrollbarBounds(Rect bounds, PanelViewMode mode, out Rect scrollbarBounds)
    {
        if (mode == PanelViewMode.BriefTwoColumns)
        {
            int rowsPerColumn = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, _context.PanelOptions());
            scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 2, 1, rowsPerColumn);
            return rowsPerColumn > 0;
        }

        int visibleRows = PanelRenderer.VisibleRows(bounds, _context.PanelOptions());
        scrollbarBounds = new Rect(bounds.Right - 1, bounds.Y + 1, 1, visibleRows);
        return visibleRows > 0;
    }

}
