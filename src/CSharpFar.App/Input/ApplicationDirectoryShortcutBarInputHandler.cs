using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationDirectoryShortcutBarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationDirectoryShortcutBarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationDirectoryShortcutInteraction interaction)
    {
        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToCommittedDirectoryShortcutArgs(interaction.Shortcut.ShortcutNumber, interaction.Shortcut.Path, interaction.Side)));
    }
}
