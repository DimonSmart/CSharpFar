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
    private readonly ApplicationDirectoryShortcutKeyboardHandler _directoryShortcutHandler;
    private readonly ApplicationFunctionKeyKeyboardHandler _functionKeyHandler;
    private readonly ApplicationKeyboardTargetResolver _targetResolver;
    private readonly ApplicationCommandLineKeyboardHandler _commandLineHandler;
    private readonly ApplicationPanelKeyboardHandler _panelHandler;

    public KeyboardInputRouter(KeyboardInputContext context)
    {
        _context = context;
        _globalHandler = new ApplicationGlobalKeyboardHandler(context);
        _directoryShortcutHandler = new ApplicationDirectoryShortcutKeyboardHandler(context);
        _functionKeyHandler = new ApplicationFunctionKeyKeyboardHandler(context);
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
        ApplicationKeyboardOwner owner = _targetResolver.Resolve(key, routed.Frame);
        var input = new ApplicationKeyboardInput(routed, key, owner);

        ApplicationInputHandlingResult global = _globalHandler.Handle(input);
        if (global.Handled)
            return new ApplicationInputHandlingResult(true, global.ShouldRender || functionKeyLayerChanged);

        ApplicationInputHandlingResult directoryShortcut = _directoryShortcutHandler.Handle(input);
        if (directoryShortcut.Handled)
            return new ApplicationInputHandlingResult(true, directoryShortcut.ShouldRender || functionKeyLayerChanged);

        ApplicationInputHandlingResult functionKey = _functionKeyHandler.Handle(input);
        if (functionKey.Handled)
            return new ApplicationInputHandlingResult(true, functionKey.ShouldRender || functionKeyLayerChanged);

        ApplicationInputHandlingResult owned = owner switch
        {
            ApplicationKeyboardOwner.CommandLine =>
                _commandLineHandler.Handle(input),
            ApplicationKeyboardOwner.LeftPanel =>
                _panelHandler.Handle(input, PanelSide.Left, routed.Frame.LeftPanel),
            ApplicationKeyboardOwner.RightPanel =>
                _panelHandler.Handle(input, PanelSide.Right, routed.Frame.RightPanel),
            _ => ApplicationInputHandlingResult.NotHandled,
        };

        if (owned.Handled)
            return new ApplicationInputHandlingResult(true, owned.ShouldRender || functionKeyLayerChanged);

        return functionKeyLayerChanged
            ? ApplicationInputHandlingResult.FromHandled(true)
            : ApplicationInputHandlingResult.NotHandled;
    }

    private ApplicationInputHandlingResult HandleWorkspaceModifier(
        ConsoleModifiers modifiers,
        ApplicationUiFrame? frame)
    {
        if (frame?.Mode == ApplicationWorkspaceMode.HiddenCommandLine)
        {
            return ApplicationInputHandlingResult.NotHandled;
        }

        return _context.SetFunctionKeyLayer(modifiers)
            ? ApplicationInputHandlingResult.FromHandled(true)
            : ApplicationInputHandlingResult.NotHandled;
    }
}
