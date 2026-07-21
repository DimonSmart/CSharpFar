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
    Rect? ScrollbarBounds,
    IReadOnlyList<TopMenuPointerTarget> PointerTargets);

internal enum TopMenuPointerActionKind
{
    ActivateForPanel,
    OpenTopItem,
    ActivateDropdownItem,
    ConsumeDropdownSurface,
    Scrollbar,
}

internal readonly record struct TopMenuPointerAction(
    TopMenuPointerActionKind Kind,
    int ItemIndex = -1);

internal sealed record TopMenuPointerTarget(
    UiTargetId Target,
    Rect Bounds,
    TopMenuPointerAction Action);

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
                null,
                []);
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

        Rect activationBounds = new(0, 0, context.Size.Width, context.Size.Height > 0 ? 1 : 0);
        Rect? scrollbarBounds = ScrollbarBounds(definition, _context.MenuState.ActiveTopMenuIndex, layout);
        return new TopMenuFrame(
            true,
            open,
            context.Viewport,
            definition,
            layout,
            _context.ActiveSide(),
            _context.MenuState.ActiveTopMenuIndex,
            _context.MenuState.ActiveDropdownItemIndex,
            activationBounds,
            scrollbarBounds,
            BuildPointerTargets(
                open,
                definition,
                layout,
                _context.MenuState.ActiveTopMenuIndex,
                activationBounds,
                scrollbarBounds));
    }

    protected override UiInteractionFrame BuildInteractionFrame(TopMenuFrame frame)
    {
        if (!frame.Available)
            return UiInteractionFrame.Empty;

        var builder = new UiInteractionFrameBuilder();
        foreach (TopMenuPointerTarget target in frame.PointerTargets)
            builder.AddHitRegion(target.Target, target.Bounds);

        if (!frame.Open)
            return builder.Build();

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
        if (!frame.Available)
            return UiInputResult.NotHandled;

        TopMenuPointerAction? action = FindPointerAction(frame, route.Target);
        if (route.IsCapturedRoute)
        {
            if (action is not { Kind: TopMenuPointerActionKind.Scrollbar })
                return UiInputResult.NotHandled;

            return RouteScrollbar(mouse, frame, captured: true);
        }

        if (action is { Kind: TopMenuPointerActionKind.Scrollbar })
            return RouteScrollbar(mouse, frame, captured: false);

        if (!frame.Open)
        {
            if (!IsLeftMouseDown(mouse) || action is null)
                return UiInputResult.NotHandled;

            _context.PanelQuickSearch.Close();
            _controller.HandlePointerAction(action.Value, frame.Definition, frame.ActivePanelSide);
            return UiInputResult.HandledAndInvalidate;
        }

        if (!IsLeftMouseDown(mouse))
            return UiInputResult.HandledResult;

        if (action is null)
        {
            _controller.Close();
            return UiInputResult.HandledAndInvalidate;
        }

        _controller.HandlePointerAction(action.Value, frame.Definition, frame.ActivePanelSide);
        return UiInputResult.HandledAndInvalidate;
    }

    private UiInputResult RouteScrollbar(MouseConsoleInputEvent mouse, TopMenuFrame frame, bool captured)
    {
        bool handled = _controller.HandleDropdownScrollbarMouse(mouse, frame.Definition, frame.Layout);

        if (handled && mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, invalidate: true);

        if (captured && mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Up)
            return UiInputResult.ReleaseMouse(invalidate: true);

        return handled ? UiInputResult.HandledAndInvalidate : UiInputResult.NotHandled;
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

    private static IReadOnlyList<TopMenuPointerTarget> BuildPointerTargets(
        bool open,
        MenuBarDefinition definition,
        MenuLayout layout,
        int activeTopMenuIndex,
        Rect activationBounds,
        Rect? scrollbarBounds)
    {
        var targets = new List<TopMenuPointerTarget>();
        if (!open)
        {
            targets.Add(new(ActivationTarget, activationBounds, new(TopMenuPointerActionKind.ActivateForPanel)));
            for (int i = 0; i < layout.TopItemBounds.Count && i < definition.Items.Count; i++)
                targets.Add(new(TopTarget(definition.Items[i].Id), layout.TopItemBounds[i], new(TopMenuPointerActionKind.OpenTopItem, i)));
            return targets;
        }

        for (int i = 0; i < layout.TopItemBounds.Count && i < definition.Items.Count; i++)
            targets.Add(new(TopTarget(definition.Items[i].Id), layout.TopItemBounds[i], new(TopMenuPointerActionKind.OpenTopItem, i)));

        if (layout.DropdownBounds is not { } dropdown || activeTopMenuIndex < 0 || activeTopMenuIndex >= definition.Items.Count)
            return targets;

        targets.Add(new(new UiTargetId("application.top-menu.border"), dropdown, new(TopMenuPointerActionKind.ConsumeDropdownSurface)));
        var children = definition.Items[activeTopMenuIndex].Children;
        int visibleRows = Math.Max(0, dropdown.Height - 2);
        for (int row = 0; row < visibleRows; row++)
        {
            int itemIndex = layout.DropdownFirstVisibleItemIndex + row;
            if (itemIndex < 0 || itemIndex >= children.Count)
                break;

            targets.Add(new(
                DropdownTarget(definition.Items[activeTopMenuIndex].Id, itemIndex),
                new Rect(dropdown.X + 1, dropdown.Y + 1 + row, Math.Max(0, dropdown.Width - 2), 1),
                new(TopMenuPointerActionKind.ActivateDropdownItem, itemIndex)));
        }

        if (scrollbarBounds is { } scrollbar)
            targets.Add(new(ScrollbarTarget, scrollbar, new(TopMenuPointerActionKind.Scrollbar)));

        return targets;
    }

    private static TopMenuPointerAction? FindPointerAction(TopMenuFrame frame, UiTargetId? target)
    {
        if (target is null)
            return null;

        foreach (TopMenuPointerTarget pointerTarget in frame.PointerTargets)
            if (pointerTarget.Target == target)
                return pointerTarget.Action;

        return null;
    }

    private static bool IsLeftMouseDown(MouseConsoleInputEvent mouse) =>
        mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down;

    private static bool IsPlainKey(ConsoleKeyInfo key, ConsoleKey consoleKey) =>
        key.Key == consoleKey && key.Modifiers == 0;
}
