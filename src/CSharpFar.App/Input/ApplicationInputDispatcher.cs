using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationInputDispatcher
{
    private readonly Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> _handleKeyInput;
    private readonly Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> _handleModifierInput;
    private readonly MouseInputRouter _mouseInputRouter;
    private readonly ApplicationCommandLineInputHandler _commandLineInputHandler;

    public ApplicationInputDispatcher(
        Func<ConsoleKeyInfo, ApplicationRuntimeRenderRequest> handleKeyInput,
        Func<ConsoleModifiers, ApplicationRuntimeRenderRequest> handleModifierInput,
        MouseInputRouter mouseInputRouter,
        ApplicationCommandLineInputHandler commandLineInputHandler)
    {
        _handleKeyInput = handleKeyInput;
        _handleModifierInput = handleModifierInput;
        _mouseInputRouter = mouseInputRouter;
        _commandLineInputHandler = commandLineInputHandler;
    }

    public ApplicationRuntimeRenderRequest Handle(UiRoutedInput<ApplicationUiFrame> routed) =>
        routed.Input switch
        {
            KeyConsoleInputEvent { Key: var key } => _handleKeyInput(key),
            ModifierKeyConsoleInputEvent { Modifiers: var modifiers } => _handleModifierInput(modifiers),
            MouseConsoleInputEvent mouse => HandleMouse(routed, mouse),
            _ => ApplicationRuntimeRenderRequest.None,
        };

    private ApplicationRuntimeRenderRequest HandleMouse(
        UiRoutedInput<ApplicationUiFrame> routed,
        MouseConsoleInputEvent mouse)
    {
        if (routed.Target == ApplicationTargetIds.CommandLine)
        {
            ApplicationInputHandlingResult result = _commandLineInputHandler.Handle(
                mouse,
                routed.Frame.CommandLine,
                routed.RouteKind);

            if (result.Handled)
                return new ApplicationRuntimeRenderRequest(result.ShouldRender);
        }

        bool handled = _mouseInputRouter.Handle(mouse, routed.Frame);
        return new ApplicationRuntimeRenderRequest(handled);
    }
}
