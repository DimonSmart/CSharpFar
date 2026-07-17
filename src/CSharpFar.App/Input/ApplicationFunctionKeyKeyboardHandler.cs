using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationFunctionKeyKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationFunctionKeyKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationKeyboardInput input)
    {
        ConsoleKeyInfo key = input.Key;
        if (key.Key is < ConsoleKey.F1 or > ConsoleKey.F12)
            return ApplicationInputHandlingResult.NotHandled;

        if (!FunctionKeyLayerResolver.TryResolveChordLayer(key.Modifiers, out var layer))
            return ApplicationInputHandlingResult.NotHandled;

        var hit = input.Frame.FunctionKeyBar?.Actions.FirstOrDefault(action =>
            action.Layer == layer &&
            action.Key == key.Key);
        if (hit is null)
            return ApplicationInputHandlingResult.NotHandled;

        bool shouldRender = _context.ExecuteRegisteredCommand(
            hit.CommandId,
            new ApplicationPanelCommandInvocation(
                input.ActiveSide,
                input.ActivePanelFrame?.VisibleRows ?? 0,
                input.ActivePanel,
                input.Panel(OtherPanelSide(input.ActiveSide))));
        return ApplicationInputHandlingResult.FromHandled(shouldRender);
    }

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;
}
