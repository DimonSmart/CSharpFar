using CSharpFar.App.Rendering;

namespace CSharpFar.App.Input;

internal sealed class ApplicationQuickViewInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationQuickViewInputHandler(MouseInputContext context) => _context = context;

    public ApplicationInputHandlingResult Handle(ApplicationQuickViewPointerInteraction interaction) =>
        ApplicationInputHandlingResult.FromHandled(interaction.Target switch
        {
            ApplicationQuickViewMonitorToggleTarget => _context.ToggleQuickViewDirectoryMonitor(),
            ApplicationQuickViewChangeTarget change => _context.ActivateQuickViewDirectoryMonitorChange(change.ChangeId),
            _ => false,
        });

    public ApplicationInputHandlingResult Handle(ApplicationQuickViewListInteraction interaction) =>
        ApplicationInputHandlingResult.FromHandled(_context.HandleQuickViewRecentChangesInput(interaction.Confirmed));

    public bool SelectFileUsageOwner(int ownerIndex) => _context.SelectFileUsageOwner(ownerIndex);
}

internal sealed record ApplicationQuickViewListInteraction(bool Confirmed) : ApplicationPointerInteraction;
