using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class KeyboardInputRouter
{
    private readonly KeyboardInputContext _context;
    private readonly ApplicationGlobalKeyboardHandler _globalHandler;
    private readonly ApplicationKeyboardTargetResolver _targetResolver;
    private readonly ApplicationCommandLineKeyboardHandler _commandLineHandler;
    private readonly ApplicationPanelKeyboardHandler _panelHandler;

    public KeyboardInputRouter(KeyboardInputContext context)
    {
        _context = context;
        _globalHandler = new ApplicationGlobalKeyboardHandler(context);
        _targetResolver = new ApplicationKeyboardTargetResolver();
        _commandLineHandler = new ApplicationCommandLineKeyboardHandler(context);
        _panelHandler = new ApplicationPanelKeyboardHandler(context);
    }

    public ApplicationInputHandlingResult Handle(UiRoutedInput<ApplicationUiFrame> routed)
    {
        if (routed.Input is ModifierKeyConsoleInputEvent { Modifiers: var modifiers })
        {
            if (routed.Target != ApplicationTargetIds.WorkspaceKeyboard ||
                routed.RouteKind != UiInputRouteKind.KeyboardTarget)
            {
                return ApplicationInputHandlingResult.NotHandled;
            }

            return HandleWorkspaceModifier(modifiers, routed.Frame);
        }

        if (routed.Input is not KeyConsoleInputEvent { Key: var key })
            return ApplicationInputHandlingResult.NotHandled;

        if (routed.Target != ApplicationTargetIds.WorkspaceKeyboard ||
            routed.RouteKind != UiInputRouteKind.KeyboardTarget)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        bool functionKeyLayerChanged = _context.SetFunctionKeyLayer(key.Modifiers);

        ApplicationInputHandlingResult global = _globalHandler.Handle(key, routed.Frame);
        if (global.Handled)
            return new ApplicationInputHandlingResult(true, global.ShouldRender || functionKeyLayerChanged);

        ApplicationKeyboardOwner owner = _targetResolver.Resolve(key, routed.Frame);
        ApplicationInputHandlingResult owned = owner switch
        {
            ApplicationKeyboardOwner.CommandLine =>
                _commandLineHandler.Handle(key, routed.Frame),
            ApplicationKeyboardOwner.LeftPanel =>
                _panelHandler.Handle(key, PanelSide.Left, routed.Frame.LeftPanel),
            ApplicationKeyboardOwner.RightPanel =>
                _panelHandler.Handle(key, PanelSide.Right, routed.Frame.RightPanel),
            _ => ApplicationInputHandlingResult.NotHandled,
        };

        if (owned.Handled)
            return new ApplicationInputHandlingResult(true, owned.ShouldRender || functionKeyLayerChanged);

        return functionKeyLayerChanged
            ? ApplicationInputHandlingResult.FromHandled(true)
            : ApplicationInputHandlingResult.NotHandled;
    }

    public ApplicationInputHandlingResult HandleModifier(ConsoleModifiers modifiers)
    {
        return HandleWorkspaceModifier(modifiers, null);
    }

    private ApplicationInputHandlingResult HandleWorkspaceModifier(
        ConsoleModifiers modifiers,
        ApplicationUiFrame? frame)
    {
        if (frame?.Mode == ApplicationWorkspaceMode.HiddenCommandLine ||
            (frame is null && !_context.IsPanelsMode()))
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        return _context.SetFunctionKeyLayer(modifiers)
            ? ApplicationInputHandlingResult.FromHandled(true)
            : ApplicationInputHandlingResult.NotHandled;
    }
}
