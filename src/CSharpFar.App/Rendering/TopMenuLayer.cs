using CSharpFar.App.Menu;
using CSharpFar.App.State;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed record TopMenuFrame(
    bool Available,
    bool Open,
    ConsoleViewport Viewport,
    MenuBarDefinition Definition,
    MenuLayout Layout,
    PanelSide ActivePanelSide,
    int ActiveTopMenuIndex,
    int ActiveDropdownItemIndex,
    Rect ActivationBounds,
    Rect? ScrollbarBounds);

internal sealed class TopMenuLayer : UiLayer<TopMenuFrame>
{
    private static readonly UiTargetId ActivationTarget = new("application.top-menu.activation");
    private static readonly UiTargetId ScrollbarTarget = new("application.top-menu.scrollbar");

    private readonly ApplicationRenderContext _context;
    private readonly TopMenuController _controller;
    private readonly MenuLayoutService _layoutService;

    public TopMenuLayer(
        ApplicationRenderContext context,
        TopMenuController controller,
        MenuLayoutService layoutService)
    {
        _context = context;
        _controller = controller;
        _layoutService = layoutService;
    }

    public override UiLayerInputPolicy InputPolicy =>
        _context.App.WorkspaceMode != ApplicationWorkspaceMode.Panels
            ? UiLayerInputPolicy.None
            : _context.MenuState.OpenState == MenuOpenState.Closed
            ? UiLayerInputPolicy.Bubble
            : UiLayerInputPolicy.Modal;

    protected override TopMenuFrame RenderFrame(UiRenderContext context)
    {
        if (_context.App.WorkspaceMode != ApplicationWorkspaceMode.Panels)
        {
            return new TopMenuFrame(
                false,
                false,
                context.Viewport,
                new MenuBarDefinition { Items = [] },
                new MenuLayout
                {
                    TopItemBounds = [],
                    DropdownBounds = null,
                    DropdownFirstVisibleItemIndex = -1,
                },
                _context.ActiveSide(),
                _context.MenuState.ActiveTopMenuIndex,
                _context.MenuState.ActiveDropdownItemIndex,
                default,
                null);
        }

        var definition = _context.BuildMenuDefinition();
        var bounds = new Rect(0, 0, context.Size.Width, context.Size.Height);
        var layout = _layoutService.CalculateLayout(bounds, definition, _context.MenuState);
        bool open = _context.MenuState.OpenState != MenuOpenState.Closed;
        if (open)
        {
            var options = MenuRenderOptionsFactory.Create(_context.App.Palette);
            new MenuBarRenderer().Render(context.Screen, bounds, definition, _context.MenuState, layout, options);
            new DropdownMenuRenderer(_layoutService).Render(
                context.Screen,
                definition,
                _context.MenuState,
                layout,
                options);
        }

        return new TopMenuFrame(
            true,
            open,
            context.Viewport,
            definition,
            layout,
            _context.ActiveSide(),
            _context.MenuState.ActiveTopMenuIndex,
            _context.MenuState.ActiveDropdownItemIndex,
            new Rect(0, 0, context.Size.Width, context.Size.Height > 0 ? 1 : 0),
            ScrollbarBounds(definition, _context.MenuState.ActiveTopMenuIndex, layout));
    }

    protected override UiInteractionFrame BuildInteractionFrame(TopMenuFrame frame)
    {
        if (!frame.Available)
            return UiInteractionFrame.Empty;

        var builder = new UiInteractionFrameBuilder();
        if (!frame.Open)
        {
            return builder.AddHitRegion(ActivationTarget, frame.ActivationBounds).Build();
        }

        for (int i = 0; i < frame.Layout.TopItemBounds.Count; i++)
            builder.AddHitRegion(TopTarget(frame.Definition.Items[i].Id), frame.Layout.TopItemBounds[i]);

        if (frame.Layout.DropdownBounds is { } dropdown &&
            frame.ActiveTopMenuIndex >= 0 &&
            frame.ActiveTopMenuIndex < frame.Definition.Items.Count)
        {
            builder.AddHitRegion(new UiTargetId("application.top-menu.border"), dropdown);
            var children = frame.Definition.Items[frame.ActiveTopMenuIndex].Children;
            int visibleRows = Math.Max(0, dropdown.Height - 2);
            for (int row = 0; row < visibleRows; row++)
            {
                int itemIndex = frame.Layout.DropdownFirstVisibleItemIndex + row;
                if (itemIndex >= children.Count)
                    break;

                builder.AddHitRegion(new UiHitRegion(
                    DropdownTarget(frame.Definition.Items[frame.ActiveTopMenuIndex].Id, itemIndex),
                    new Rect(dropdown.X + 1, dropdown.Y + 1 + row, Math.Max(0, dropdown.Width - 2), 1)));
            }
        }

        if (frame.ScrollbarBounds is { } scrollbar)
            builder.AddHitRegion(ScrollbarTarget, scrollbar);

        UiTargetId focusTarget = ActiveTarget(frame);
        return builder
            .AddFocusEntry(focusTarget, 0)
            .SetDefaultFocusTarget(focusTarget)
            .Build();
    }

    protected override void OnFrameCommitted(TopMenuFrame frame)
    {
        int childCount = frame.Available &&
            frame.Open &&
            frame.ActiveTopMenuIndex >= 0 &&
            frame.ActiveTopMenuIndex < frame.Definition.Items.Count
            ? frame.Definition.Items[frame.ActiveTopMenuIndex].Children.Count
            : 0;
        int visibleRows = frame.Layout.DropdownBounds is { } dropdown
            ? Math.Max(0, dropdown.Height - 2)
            : 0;
        _controller.CommitDropdownScrollbar(frame.ScrollbarBounds, childCount, visibleRows);
    }

    protected override UiInputResult RouteInput(
        ConsoleInputEvent input,
        TopMenuFrame frame,
        UiInputRouteContext context)
    {
        return input switch
        {
            KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
            MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
            _ => UiInputResult.NotHandled,
        };
    }

    private UiInputResult RouteKey(ConsoleKeyInfo key, TopMenuFrame frame)
    {
        if (!frame.Open && !IsPlainKey(key, ConsoleKey.F9))
            return UiInputResult.NotHandled;

        if (!frame.Available)
            return UiInputResult.NotHandled;

        if (!frame.Open)
            _context.PanelQuickSearch.Close();

        bool handled = _controller.HandleKey(key, frame.Definition, frame.ActivePanelSide);
        return handled ? UiInputResult.HandledAndInvalidate : UiInputResult.NotHandled;
    }

    private UiInputResult RouteMouse(
        MouseConsoleInputEvent mouse,
        TopMenuFrame frame,
        UiInputRouteContext route)
    {
        if ((route.Target == ScrollbarTarget || route.IsCapturedRoute) && frame.Open)
            return RouteScrollbar(mouse, frame, route.IsCapturedRoute);

        if (!frame.Available)
            return UiInputResult.NotHandled;

        if (!frame.Open)
        {
            if (mouse.Y != 0 ||
                mouse.Button != MouseButton.Left ||
                mouse.Kind != MouseEventKind.Down)
            {
                return UiInputResult.NotHandled;
            }

            _context.PanelQuickSearch.Close();
        }

        bool handled = _controller.HandleMouse(mouse, frame.Definition, frame.Layout, frame.ActivePanelSide);
        return handled ? UiInputResult.HandledAndInvalidate : UiInputResult.NotHandled;
    }

    private UiInputResult RouteScrollbar(MouseConsoleInputEvent mouse, TopMenuFrame frame, bool captured)
    {
        bool handled = _controller.HandleMouse(mouse, frame.Definition, frame.Layout, frame.ActivePanelSide);
        if (!handled)
            return UiInputResult.NotHandled;

        if (mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true);

        if (captured && mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Up)
            return UiInputResult.ReleaseMouse(invalidate: true);

        return UiInputResult.HandledAndInvalidate;
    }

    private static Rect? ScrollbarBounds(MenuBarDefinition definition, int activeTopMenuIndex, MenuLayout layout)
    {
        if (layout.DropdownBounds is not { } dropdown ||
            layout.DropdownFirstVisibleItemIndex < 0 ||
            activeTopMenuIndex < 0 ||
            activeTopMenuIndex >= definition.Items.Count)
        {
            return null;
        }

        int visibleRows = Math.Max(0, dropdown.Height - 2);
        int childCount = definition.Items[activeTopMenuIndex].Children.Count;
        var bounds = new Rect(dropdown.Right - 1, dropdown.Y + 1, 1, visibleRows);
        var state = new ScrollState
        {
            TotalItems = childCount,
            ViewportItems = visibleRows,
            FirstVisibleIndex = layout.DropdownFirstVisibleItemIndex,
        };
        if (!ScrollBarInteraction.IsInteractive(bounds, state))
            return null;

        return bounds;
    }

    private static UiTargetId ActiveTarget(TopMenuFrame frame)
    {
        if (frame.ActiveTopMenuIndex >= 0 &&
            frame.ActiveTopMenuIndex < frame.Definition.Items.Count)
        {
            var top = frame.Definition.Items[frame.ActiveTopMenuIndex];
            if (frame.ActiveDropdownItemIndex >= 0 &&
                frame.ActiveDropdownItemIndex < top.Children.Count &&
                top.Children[frame.ActiveDropdownItemIndex].Kind != MenuItemKind.Separator &&
                top.Children[frame.ActiveDropdownItemIndex].IsEnabled)
            {
                return DropdownTarget(top.Id, frame.ActiveDropdownItemIndex);
            }

            return TopTarget(top.Id);
        }

        return ActivationTarget;
    }

    private static UiTargetId TopTarget(string id) =>
        new($"application.top-menu.top:{id}");

    private static UiTargetId DropdownTarget(string topId, int itemIndex) =>
        new($"application.top-menu.dropdown:{topId}:{itemIndex}");

    private static bool IsPlainKey(ConsoleKeyInfo key, ConsoleKey consoleKey) =>
        key.Key == consoleKey && key.Modifiers == 0;
}
