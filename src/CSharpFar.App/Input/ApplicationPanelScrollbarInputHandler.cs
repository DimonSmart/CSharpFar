using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationPanelScrollbarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationPanelScrollbarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationPanelScrollInteraction interaction)
    {
        PanelSide side = interaction.Side;
        var state = _context.GetPanelState(side);

        _context.SetActiveSide(side);
        _context.PanelController.ScrollView(
            state,
            interaction.FirstVisibleIndex - state.ScrollOffset,
            interaction.ViewportItems);
        _context.Mouse.LastLeftPanelItemClick = null;
        return ApplicationInputHandlingResult.FromHandled(shouldRender: true);
    }
}
