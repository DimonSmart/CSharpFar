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
        ApplicationFunctionKeyBarFrame? frame,
        UiInputRouteKind routeKind)
    {
        if (routeKind != UiInputRouteKind.HitTarget ||
            input.Button != MouseButton.Left ||
            input.Kind != MouseEventKind.Down ||
            frame is null)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        var hit = frame.Actions.FirstOrDefault(action => action.Bounds.Contains(input.X, input.Y));
        if (hit is null)
            return ApplicationInputHandlingResult.NotHandled;

        return ApplicationInputHandlingResult.FromHandled(
            _context.ExecuteRegisteredCommand(hit.CommandId, null));
    }
}
