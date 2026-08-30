using CSharpFar.App.Rendering;

namespace CSharpFar.App.Input;

internal sealed class ApplicationQuickViewInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationQuickViewInputHandler(MouseInputContext context) => _context = context;

    public ApplicationInputHandlingResult Handle(ApplicationQuickViewChangeInteraction interaction) =>
        ApplicationInputHandlingResult.FromHandled(_context.ActivateQuickViewDirectoryMonitorChange(interaction.ChangeId));
}
