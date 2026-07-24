using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationFunctionKeyBarInputHandler
{
    private readonly MouseInputContext _context;

    public ApplicationFunctionKeyBarInputHandler(MouseInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(
        MouseConsoleInputEvent input,
        ApplicationUiFrame frame,
        ApplicationFunctionKeyHit action,
        UiInputRouteKind routeKind)
    {
        if (routeKind != UiInputRouteKind.HitTarget ||
            input.Button != MouseButton.Left ||
            input.Kind != MouseEventKind.Down)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(action.CommandId, ApplicationPanelCommandInvocationFactory.Create(frame)));
    }
}
