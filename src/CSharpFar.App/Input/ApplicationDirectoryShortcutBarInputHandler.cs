using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationDirectoryShortcutBarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationDirectoryShortcutBarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationDirectoryShortcutHit shortcut,
        PanelSide side,
        UiInputRouteKind routeKind)
    {
        if (routeKind != UiInputRouteKind.HitTarget ||
            input.Button != MouseButton.Left ||
            input.Kind != MouseEventKind.Down)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToCommittedDirectoryShortcutArgs(shortcut.ShortcutNumber, shortcut.Path, side)));
    }
}
