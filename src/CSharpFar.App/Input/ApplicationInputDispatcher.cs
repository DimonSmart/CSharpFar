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
        ApplicationInputHandlingResult result;
        if (packet.Target == ApplicationTargetIds.CommandLine)
        {
            result = _commandLineInputHandler.Handle(
                mouse,
                packet.Frame.CommandLine,
                packet.RouteKind);
        }
        else if (packet.Target == ApplicationTargetIds.LeftPanelScrollbar ||
                 packet.Target == ApplicationTargetIds.RightPanelScrollbar)
        {
            result = _panelScrollbarInputHandler.Handle(
                packet.ScrollbarInput,
                packet.RouteKind);
        }
        else if (packet.Frame.FunctionKeyBar is { } functionKeyBar &&
                 TryResolveFunctionKeyPointer(functionKeyBar, packet.Target, out ApplicationFunctionKeyHit functionKey))
        {
            result = _functionKeyBarInputHandler.Handle(
                mouse,
                packet.Frame,
                functionKey,
                packet.RouteKind);
        }
        else if (packet.Frame.DirectoryShortcutBar is { } shortcutBar &&
                 TryResolveDirectoryShortcutPointer(shortcutBar, packet.Target, out ApplicationDirectoryShortcutHit shortcut))
        {
            result = _directoryShortcutBarInputHandler.Handle(
                mouse,
                shortcut,
                packet.Frame.Keyboard.ActiveSide,
                packet.RouteKind);
        }
        else if (packet.Frame.LeftPanel is { } leftPanel && leftPanel.OwnsPointerTarget(packet.Target))
        {
            result = _panelInputHandler.Handle(
                mouse,
                leftPanel,
                packet.RouteKind,
                packet.Target);
        }
        else if (packet.Frame.RightPanel is { } rightPanel && rightPanel.OwnsPointerTarget(packet.Target))
        {
            result = _panelInputHandler.Handle(
                mouse,
                rightPanel,
                packet.RouteKind,
                packet.Target);
        }
        else
        {
            result = ApplicationInputHandlingResult.NotHandled;
        }

        return result.Handled
            ? new ApplicationRuntimeRenderRequest(result.ShouldRender)
            : ApplicationRuntimeRenderRequest.None;
    }

    private static bool TryResolveFunctionKeyPointer(
        ApplicationFunctionKeyBarFrame frame,
        UiTargetId? target,
        out ApplicationFunctionKeyHit action)
    {
        return frame.TryGetPointerAction(target, out action);
    }

    private static bool TryResolveDirectoryShortcutPointer(
        ApplicationDirectoryShortcutBarFrame frame,
        UiTargetId? target,
        out ApplicationDirectoryShortcutHit shortcut)
    {
        return frame.TryGetPointerShortcut(target, out shortcut);
    }
}
