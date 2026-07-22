using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record PanelQuickSearchFrame(
    bool Active,
    bool PopupVisible,
    ConsoleViewport Viewport,
    PanelSide? PanelSide,
    Rect PopupBounds,
    Rect InputBounds,
    UiCursorPlacement? Cursor,
    string SearchText);

internal sealed class PanelQuickSearchLayer : UiLayer<PanelQuickSearchFrame>
{
    private static readonly UiTargetId InputTarget = new("application.panel-quick-search.input");

    private readonly ApplicationRenderContext _context;
    private readonly Action<bool> _hideCompletion;
    private readonly Action _resetHistoryNavigation;

    public PanelQuickSearchLayer(
        ApplicationRenderContext context,
        Action<bool> hideCompletion,
        Action resetHistoryNavigation)
    {
        _context = context;
        _hideCompletion = hideCompletion;
        _resetHistoryNavigation = resetHistoryNavigation;
    }

    public override UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.Bubble;

    protected override PanelQuickSearchFrame RenderFrame(UiRenderContext context)
    {
        if (_context.PanelQuickSearch.State is not { } quickSearch ||
            _context.App.WorkspaceMode != ApplicationWorkspaceMode.Panels)
        {
            return Hidden(context.Viewport);
        }

        var workspace = ApplicationLayoutService.CalculatePanelWorkspaceBounds(context.Size);
        var panelBounds = quickSearch.PanelSide == PanelSide.Left
            ? workspace.Left
            : workspace.Right;
        var layout = new PanelQuickSearchRenderer(context.Canvas, _context.App.Palette)
            .Render(panelBounds, quickSearch.SearchText);
        if (layout is null)
            return new PanelQuickSearchFrame(
                true,
                false,
                context.Viewport,
                quickSearch.PanelSide,
                default,
                default,
                null,
                quickSearch.SearchText);

        return new PanelQuickSearchFrame(
            true,
            true,
            context.Viewport,
            quickSearch.PanelSide,
            layout.PopupBounds,
            layout.InputBounds,
            layout.Cursor,
            quickSearch.SearchText);
    }

    protected override UiInteractionFrame BuildInteractionFrame(PanelQuickSearchFrame frame)
    {
        if (!frame.Active)
            return UiInteractionFrame.Empty;

        var builder = new UiInteractionFrameBuilder()
            .AddFocusEntry(InputTarget, 0, isEnabled: true, frame.Cursor)
            .SetDefaultFocusTarget(InputTarget);

        if (frame.PopupVisible)
            builder.AddHitRegion(InputTarget, frame.InputBounds);

        return builder.Build();
    }

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        PanelQuickSearchFrame frame,
        UiInputRouteContext context)
    {
        return input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };
    }

    private UiInputResult RouteKey(ConsoleKeyInfo key, PanelQuickSearchFrame frame)
    {
        if (!frame.Active)
        {
            if ((key.Modifiers & ConsoleModifiers.Alt) == 0 ||
                (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                return UiInputResult.NotHandled;
            }

            if (!_context.PanelQuickSearch.TryStart(key))
                return UiInputResult.NotHandled;

            _hideCompletion(false);
            _resetHistoryNavigation();
            return UiInputResult.HandledAndInvalidate;
        }

        var result = _context.PanelQuickSearch.HandleKey(key);
        return result switch
        {
            PanelQuickSearchKeyResult.Handled => UiInputResult.HandledAndInvalidate,
            PanelQuickSearchKeyResult.CloseAndContinue => UiInputResult.InvalidateOnly(),
            _ => UiInputResult.NotHandled,
        };
    }

    private UiInputResult RouteMouse(
        MouseConsoleInputEvent mouse,
        PanelQuickSearchFrame frame,
        UiInputRouteContext route)
    {
        if (!frame.Active)
            return UiInputResult.NotHandled;

        if (route.Target == InputTarget)
            return UiInputResult.HandledResult;

        if (mouse.Kind is MouseEventKind.Move or MouseEventKind.Up or MouseEventKind.Wheel)
            return UiInputResult.NotHandled;

        if (mouse is not { Button: MouseButton.Left, Kind: MouseEventKind.Down })
            return UiInputResult.NotHandled;

        _context.PanelQuickSearch.Close();
        // Closing the transient overlay redraws it away while leaving this click for the layer below.
        return UiInputResult.InvalidateOnly();
    }

    private static PanelQuickSearchFrame Hidden(ConsoleViewport viewport) =>
        new(false, false, viewport, null, default, default, null, string.Empty);
}
