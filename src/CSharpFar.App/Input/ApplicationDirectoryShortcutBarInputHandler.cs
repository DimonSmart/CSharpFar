using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
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
        ApplicationDirectoryShortcutBarFrame? frame,
        UiInputRouteKind routeKind)
    {
        if (routeKind != UiInputRouteKind.HitTarget ||
            input.Button != MouseButton.Left ||
            input.Kind != MouseEventKind.Down ||
            frame is null)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        var hit = frame.Shortcuts.FirstOrDefault(shortcut => shortcut.Bounds.Contains(input.X, input.Y));
        if (hit is null)
            return ApplicationInputHandlingResult.NotHandled;

        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToDirectoryShortcutArgs(hit.ShortcutNumber)));
    }
}
