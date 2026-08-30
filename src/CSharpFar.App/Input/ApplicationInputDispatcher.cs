using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;

namespace CSharpFar.App.Input;

internal sealed class ApplicationInputDispatcher
{
    private readonly KeyboardInputRouter _keyboardInputRouter;
    private readonly ApplicationCommandLineInputHandler _commandLineInputHandler;
    private readonly ApplicationPanelInputHandler _panelInputHandler;
    private readonly ApplicationPanelScrollbarInputHandler _panelScrollbarInputHandler;
    private readonly ApplicationFunctionKeyBarInputHandler _functionKeyBarInputHandler;
    private readonly ApplicationDirectoryShortcutBarInputHandler _directoryShortcutBarInputHandler;
    private readonly ApplicationQuickViewInputHandler? _quickViewInputHandler;

    public ApplicationInputDispatcher(
        KeyboardInputRouter keyboardInputRouter,
        ApplicationCommandLineInputHandler commandLineInputHandler,
        ApplicationPanelInputHandler panelInputHandler,
        ApplicationPanelScrollbarInputHandler panelScrollbarInputHandler,
        ApplicationFunctionKeyBarInputHandler functionKeyBarInputHandler,
        ApplicationDirectoryShortcutBarInputHandler directoryShortcutBarInputHandler,
        ApplicationQuickViewInputHandler? quickViewInputHandler = null)
    {
        _keyboardInputRouter = keyboardInputRouter;
        _commandLineInputHandler = commandLineInputHandler;
        _panelInputHandler = panelInputHandler;
        _panelScrollbarInputHandler = panelScrollbarInputHandler;
        _functionKeyBarInputHandler = functionKeyBarInputHandler;
        _directoryShortcutBarInputHandler = directoryShortcutBarInputHandler;
        _quickViewInputHandler = quickViewInputHandler;
    }

    public ApplicationRuntimeRenderRequest Handle(ApplicationUiInputPacket packet)
    {
        ApplicationInputHandlingResult pointerResult = packet.PointerInteraction switch
        {
            ApplicationCommandLineInteraction interaction => _commandLineInputHandler.Handle(interaction),
            ApplicationPanelInteraction interaction => _panelInputHandler.Handle(interaction),
            ApplicationPanelScrollInteraction interaction => _panelScrollbarInputHandler.Handle(interaction),
            ApplicationFunctionKeyInteraction interaction => _functionKeyBarInputHandler.Handle(interaction),
            ApplicationDirectoryShortcutInteraction interaction => _directoryShortcutBarInputHandler.Handle(interaction),
            ApplicationQuickViewChangeInteraction interaction when _quickViewInputHandler is not null => _quickViewInputHandler.Handle(interaction),
            _ => ApplicationInputHandlingResult.NotHandled,
        };
        if (pointerResult.Handled)
            return ToRuntimeRequest(pointerResult);

        return packet.Input switch
        {
            KeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(packet.Routed)),
            ModifierKeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(packet.Routed)),
            _ => ApplicationRuntimeRenderRequest.None,
        };
    }

    private static ApplicationRuntimeRenderRequest ToRuntimeRequest(ApplicationInputHandlingResult result) =>
        result.Handled
            ? new ApplicationRuntimeRenderRequest(
                result.ShouldRender,
                result.RenderParts,
                result.ResumesHiddenInteraction)
            : ApplicationRuntimeRenderRequest.None;

}
