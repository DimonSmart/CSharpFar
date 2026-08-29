using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationPanelInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationPanelInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationPanelInteraction interaction)
    {
        ApplicationPanelFrame frame = interaction.Frame;
        var state = _context.GetPanelState(frame.Side);
        ApplicationPanelItemHit? hit = interaction.Action.Item?.Item;

        if (interaction.Action is { Kind: RoutedPointerActionKind.ItemPrimaryPressed, Item.IsRetry: true })
        {
            _context.SetActiveSide(frame.Side);
            _context.SafeRefresh(state, frame.VisibleRows);
            _context.Mouse.LastLeftPanelItemClick = null;
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (interaction.Action.Kind is RoutedPointerActionKind.WheelUp or RoutedPointerActionKind.WheelDown)
        {
            _context.SetActiveSide(frame.Side);
            int delta = interaction.Action.Kind == RoutedPointerActionKind.WheelUp ? -3 : 3;
            _context.PanelController.ScrollView(state, delta, frame.VisibleRows);
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        bool hasItemTarget = hit is not null;

        if (interaction.Action.Kind == RoutedPointerActionKind.ItemSecondaryPressed)
        {
            _context.Mouse.LastLeftPanelItemClick = null;
            _context.SetActiveSide(frame.Side);
            if (hit is not null && TryGetCurrentItem(hit, state, out var item))
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

        if (interaction.Action.Kind == RoutedPointerActionKind.ItemDoubleClicked)
        {
            _context.SetActiveSide(frame.Side);
            if (hit is not null && TryGetCurrentItem(hit, state, out var item))
            {
                _context.PanelController.SetCursorTo(state, hit.ItemIndex, frame.VisibleRows);
                var currentClick = new PanelItemClick(frame.Side, hit.ItemIndex, hit.ItemLocation);
                if (_context.Mouse.LastLeftPanelItemClick == currentClick)
                    _context.OpenPanelItem(state, frame.Side, item);
            }

            _context.Mouse.LastLeftPanelItemClick = null;
            return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
        }

        if (interaction.Action.Kind is RoutedPointerActionKind.ItemPrimaryPressed or RoutedPointerActionKind.SurfacePressed)
        {
            _context.SetActiveSide(frame.Side);
            if (hit is not null && TryGetCurrentItem(hit, state, out _))
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
