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

        var action = input.Frame.FunctionKeyBar?.KeyboardActions.FirstOrDefault(candidate =>
            candidate.Layer == layer &&
            candidate.Key == key.Key);
        if (action is null)
            return ApplicationInputHandlingResult.NotHandled;

        bool shouldRender = _context.ExecuteRegisteredCommand(
            action.CommandId,
            ApplicationPanelCommandInvocationFactory.Create(input.Frame));
        return ApplicationInputHandlingResult.FromHandled(shouldRender);
    }

}
