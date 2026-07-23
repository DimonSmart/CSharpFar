using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationInputDispatcher
{
    private readonly KeyboardInputRouter _keyboardInputRouter;
    private readonly ApplicationCommandLineInputHandler _commandLineInputHandler;
    private readonly ApplicationPanelInputHandler _panelInputHandler;
    private readonly ApplicationPanelScrollbarInputHandler _panelScrollbarInputHandler;
    private readonly ApplicationFunctionKeyBarInputHandler _functionKeyBarInputHandler;
    private readonly ApplicationDirectoryShortcutBarInputHandler _directoryShortcutBarInputHandler;

    public ApplicationInputDispatcher(
        KeyboardInputRouter keyboardInputRouter,
        ApplicationCommandLineInputHandler commandLineInputHandler,
        ApplicationPanelInputHandler panelInputHandler,
        ApplicationPanelScrollbarInputHandler panelScrollbarInputHandler,
        ApplicationFunctionKeyBarInputHandler functionKeyBarInputHandler,
        ApplicationDirectoryShortcutBarInputHandler directoryShortcutBarInputHandler)
    {
        _keyboardInputRouter = keyboardInputRouter;
        _commandLineInputHandler = commandLineInputHandler;
        _panelInputHandler = panelInputHandler;
        _panelScrollbarInputHandler = panelScrollbarInputHandler;
        _functionKeyBarInputHandler = functionKeyBarInputHandler;
        _directoryShortcutBarInputHandler = directoryShortcutBarInputHandler;
    }

    public ApplicationRuntimeRenderRequest Handle(ApplicationUiInputPacket packet) =>
        packet.Input switch
        {
            KeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(packet.Routed)),
            ModifierKeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(packet.Routed)),
            MouseConsoleInputEvent mouse => HandleMouse(packet, mouse),
            _ => ApplicationRuntimeRenderRequest.None,
        };

    private static ApplicationRuntimeRenderRequest ToRuntimeRequest(ApplicationInputHandlingResult result) =>
        result.Handled
            ? new ApplicationRuntimeRenderRequest(result.ShouldRender)
            : ApplicationRuntimeRenderRequest.None;

    private ApplicationRuntimeRenderRequest HandleMouse(
        ApplicationUiInputPacket packet,
        MouseConsoleInputEvent mouse)
    {
        ApplicationInputHandlingResult result = packet.Target switch
        {
            var target when target == ApplicationTargetIds.CommandLine => _commandLineInputHandler.Handle(
                mouse,
                packet.Frame.CommandLine,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.LeftPanel => _panelInputHandler.Handle(
                mouse,
                packet.Frame.LeftPanel,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.RightPanel => _panelInputHandler.Handle(
                mouse,
                packet.Frame.RightPanel,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.LeftPanelScrollbar => _panelScrollbarInputHandler.Handle(
                packet.ScrollbarInput,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.RightPanelScrollbar => _panelScrollbarInputHandler.Handle(
                packet.ScrollbarInput,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.FunctionKeyBar => _functionKeyBarInputHandler.Handle(
                mouse,
                packet.Frame,
                packet.RouteKind),
            var target when target == ApplicationTargetIds.DirectoryShortcutBar => _directoryShortcutBarInputHandler.Handle(
                mouse,
                packet.Frame.DirectoryShortcutBar,
                packet.Frame.Keyboard.ActiveSide,
                packet.RouteKind),
            _ => ApplicationInputHandlingResult.NotHandled,
        };

        return result.Handled
            ? new ApplicationRuntimeRenderRequest(result.ShouldRender)
            : ApplicationRuntimeRenderRequest.None;
    }
}
