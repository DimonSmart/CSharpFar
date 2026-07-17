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

    public ApplicationRuntimeRenderRequest Handle(UiRoutedInput<ApplicationUiFrame> routed) =>
        routed.Input switch
        {
            KeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(routed)),
            ModifierKeyConsoleInputEvent => ToRuntimeRequest(_keyboardInputRouter.Handle(routed)),
            MouseConsoleInputEvent mouse => HandleMouse(routed, mouse),
            _ => ApplicationRuntimeRenderRequest.None,
        };

    private static ApplicationRuntimeRenderRequest ToRuntimeRequest(ApplicationInputHandlingResult result) =>
        result.Handled
            ? new ApplicationRuntimeRenderRequest(result.ShouldRender)
            : ApplicationRuntimeRenderRequest.None;

    private ApplicationRuntimeRenderRequest HandleMouse(
        UiRoutedInput<ApplicationUiFrame> routed,
        MouseConsoleInputEvent mouse)
    {
        ApplicationInputHandlingResult result = routed.Target switch
        {
            var target when target == ApplicationTargetIds.CommandLine => _commandLineInputHandler.Handle(
                mouse,
                routed.Frame.CommandLine,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.LeftPanel => _panelInputHandler.Handle(
                mouse,
                routed.Frame.LeftPanel,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.RightPanel => _panelInputHandler.Handle(
                mouse,
                routed.Frame.RightPanel,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.LeftPanelScrollbar => _panelScrollbarInputHandler.Handle(
                mouse,
                PanelSide.Left,
                routed.Frame.LeftPanel?.ScrollBar,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.RightPanelScrollbar => _panelScrollbarInputHandler.Handle(
                mouse,
                PanelSide.Right,
                routed.Frame.RightPanel?.ScrollBar,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.FunctionKeyBar => _functionKeyBarInputHandler.Handle(
                mouse,
                routed.Frame.FunctionKeyBar,
                routed.RouteKind),
            var target when target == ApplicationTargetIds.DirectoryShortcutBar => _directoryShortcutBarInputHandler.Handle(
                mouse,
                routed.Frame.DirectoryShortcutBar,
                routed.Frame.Keyboard.ActiveSide,
                routed.RouteKind),
            _ => ApplicationInputHandlingResult.NotHandled,
        };

        return result.Handled
            ? new ApplicationRuntimeRenderRequest(result.ShouldRender)
            : ApplicationRuntimeRenderRequest.None;
    }
}
