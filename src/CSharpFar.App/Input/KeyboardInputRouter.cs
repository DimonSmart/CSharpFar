using CSharpFar.App.Rendering;
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
        _targetResolver = new ApplicationKeyboardTargetResolver(context);
        _commandLineHandler = new ApplicationCommandLineKeyboardHandler(context);
        _panelHandler = new ApplicationPanelKeyboardHandler(context);
    }

    public ApplicationInputHandlingResult Handle(UiRoutedInput<ApplicationUiFrame> routed, ConsoleKeyInfo key)
    {
        bool functionKeyLayerChanged = _context.SetFunctionKeyLayer(key.Modifiers);

        ApplicationInputHandlingResult global = _globalHandler.Handle(key);
        if (global.Handled)
            return new ApplicationInputHandlingResult(true, global.ShouldRender || functionKeyLayerChanged);

        UiTargetId? owner = _targetResolver.Resolve(key, routed.Frame);
        ApplicationInputHandlingResult owned = owner switch
        {
            var target when target == ApplicationTargetIds.CommandLine =>
                _commandLineHandler.Handle(key, routed.Frame),
            var target when target == ApplicationTargetIds.LeftPanel =>
                _panelHandler.Handle(key, routed.Frame.LeftPanel),
            var target when target == ApplicationTargetIds.RightPanel =>
                _panelHandler.Handle(key, routed.Frame.RightPanel),
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
        if (!_context.IsPanelsMode())
            return ApplicationInputHandlingResult.NotHandled;

        return _context.SetFunctionKeyLayer(modifiers)
            ? ApplicationInputHandlingResult.FromHandled(true)
            : ApplicationInputHandlingResult.NotHandled;
    }
}
