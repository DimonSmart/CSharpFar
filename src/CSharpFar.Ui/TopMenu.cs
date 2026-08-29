using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public interface ICommandShortcutTextProvider
{
    string? GetPrimaryShortcutText(string commandId);
}

public sealed class NullCommandShortcutTextProvider : ICommandShortcutTextProvider
{
    public static NullCommandShortcutTextProvider Instance { get; } = new();
    private NullCommandShortcutTextProvider() { }
    public string? GetPrimaryShortcutText(string commandId) => null;
}

/// <summary>A complete interactive top-menu surface.</summary>
public sealed class TopMenu : UiLayer<TopMenuFrame>
{
    private static readonly UiTargetId ActivationTarget = new("top-menu.activation");
    private static readonly UiTargetId ScrollbarTarget = new("top-menu.scrollbar");

    private readonly Func<bool> _isAvailable;
    private readonly Func<MenuBarDefinition> _getDefinition;
    private readonly Func<MenuRenderOptions> _getRenderOptions;
    private readonly Func<string?> _getInitialTopItemId;
    private readonly Func<string?, string?> _resolveAlternateTopItemId;
    private readonly Action _opening;
    private readonly Func<MenuCommandRequest, MenuCommandResult> _executeCommand;
    private readonly MenuLayoutService _layoutService;
    private readonly VerticalScrollbarController _dropdownScrollbar = new();
    private readonly MenuState _state = new();

    public TopMenu(
        Func<bool> isAvailable,
        Func<MenuBarDefinition> getDefinition,
        Func<MenuRenderOptions> getRenderOptions,
        Func<string?> getInitialTopItemId,
        Func<string?, string?> resolveAlternateTopItemId,
        Action opening,
        Func<MenuCommandRequest, MenuCommandResult> executeCommand,
        MenuLayoutService layoutService)
    {
        _isAvailable = isAvailable ?? throw new ArgumentNullException(nameof(isAvailable));
        _getDefinition = getDefinition ?? throw new ArgumentNullException(nameof(getDefinition));
        _getRenderOptions = getRenderOptions ?? throw new ArgumentNullException(nameof(getRenderOptions));
        _getInitialTopItemId = getInitialTopItemId ?? throw new ArgumentNullException(nameof(getInitialTopItemId));
        _resolveAlternateTopItemId = resolveAlternateTopItemId ?? throw new ArgumentNullException(nameof(resolveAlternateTopItemId));
        _opening = opening ?? throw new ArgumentNullException(nameof(opening));
        _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
    }

    public bool IsOpen => _state.OpenState != MenuOpenState.Closed;

    public override UiLayerInputPolicy InputPolicy =>
        !_isAvailable() ? UiLayerInputPolicy.None : IsOpen ? UiLayerInputPolicy.Modal : UiLayerInputPolicy.Bubble;

    public void Open()
    {
        if (_isAvailable())
            OpenInitial(_getDefinition());
    }

    public void Open(string? topItemId)
    {
        if (!_isAvailable())
            return;

        MenuBarDefinition definition = _getDefinition();
        OpenDropdown(definition, FindTopIndex(definition, topItemId));
    }

    public void Close()
    {
        _state.OpenState = MenuOpenState.Closed;
        _state.ActiveDropdownItemIndex = 0;
        _state.DropdownFirstVisibleItemIndex = 0;
        _dropdownScrollbar.ApplyCommittedFrame(null);
    }

    protected override TopMenuFrame RenderFrame(UiRenderContext context)
    {
        if (!_isAvailable())
            return TopMenuFrame.Unavailable(context.Viewport);

        MenuBarDefinition definition = _getDefinition();
        Rect bounds = new(0, 0, context.Size.Width, context.Size.Height);
        MenuLayout layout = _layoutService.CalculateLayout(bounds, definition, _state);
        VerticalScrollbarFrame? scrollbar = CalculateScrollbar(definition, layout);
        if (IsOpen)
        {
            MenuRenderOptions options = _getRenderOptions();
            new MenuBarRenderer().Render(context.Canvas, bounds, definition, _state, layout, options);
            new DropdownMenuRenderer(_layoutService).Render(context.Canvas, definition, _state, layout, options, scrollbar);
        }

        Rect activationBounds = new(0, 0, context.Size.Width, context.Size.Height > 0 ? 1 : 0);
        return new TopMenuFrame(true, IsOpen, definition, layout, activationBounds, scrollbar,
            BuildPointerTargets(definition, layout, activationBounds, scrollbar?.Bounds));
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
        return builder.AddFocusEntry(focusTarget, 0).SetDefaultFocusTarget(focusTarget).Build();
    }

    protected override void OnFrameCommitted(TopMenuFrame frame)
    {
        if (IsOpen)
            _state.DropdownFirstVisibleItemIndex = frame.Layout.DropdownFirstVisibleItemIndex;
        _dropdownScrollbar.ApplyCommittedFrame(frame.DropdownScrollbar);
    }

    protected override UiInputResult RouteInput(ConsoleInputEvent input, TopMenuFrame frame, UiInputRouteContext context) => input switch
    {
        KeyConsoleInputEvent { Key: var key } => RouteKey(key, frame),
        MouseConsoleInputEvent mouse => RouteMouse(mouse, frame, context),
        _ => UiInputResult.NotHandled,
    };

    private UiInputResult RouteKey(ConsoleKeyInfo key, TopMenuFrame frame)
    {
        if (!frame.Available || (!frame.Open && !IsPlainKey(key, ConsoleKey.F9)))
            return UiInputResult.NotHandled;
        HandleKey(key, frame.Definition);
        return UiInputResult.HandledAndInvalidate;
    }

    private void HandleKey(ConsoleKeyInfo key, MenuBarDefinition definition)
    {
        if (!IsOpen && IsPlainKey(key, ConsoleKey.F9)) { OpenInitial(definition); return; }
        if (IsPlainKey(key, ConsoleKey.F9)) { OpenInitial(definition); return; }
        switch (key.Key)
        {
            case ConsoleKey.Escape: Close(); return;
            case ConsoleKey.LeftArrow: MoveTop(definition, -1); return;
            case ConsoleKey.RightArrow: MoveTop(definition, 1); return;
            case ConsoleKey.DownArrow: MoveDropdown(definition, 1); return;
            case ConsoleKey.UpArrow: MoveDropdown(definition, -1); return;
            case ConsoleKey.Home: SelectBoundary(definition, true); return;
            case ConsoleKey.End: SelectBoundary(definition, false); return;
            case ConsoleKey.Enter: ExecuteActive(definition); return;
            case ConsoleKey.Tab: OpenDropdown(definition, FindTopIndex(definition, _resolveAlternateTopItemId(CurrentTopItem(definition)?.Id))); return;
        }
        TryHandleHotKey(key.KeyChar, definition);
    }

    private UiInputResult RouteMouse(MouseConsoleInputEvent mouse, TopMenuFrame frame, UiInputRouteContext route)
    {
        if (!frame.Available)
            return UiInputResult.NotHandled;
        TopMenuPointerTarget? target = FindTarget(frame, route.Target);
        if (route.IsCapturedRoute)
            return target?.Kind == TopMenuPointerTargetKind.Scrollbar ? RouteScrollbar(mouse, frame, true) : UiInputResult.NotHandled;
        if (target?.Kind == TopMenuPointerTargetKind.Scrollbar)
            return RouteScrollbar(mouse, frame, false);
        if (!frame.Open)
        {
            if (!IsLeftMouseDown(mouse) || target is null)
                return UiInputResult.NotHandled;
            if (target.Kind == TopMenuPointerTargetKind.Activation)
                OpenInitial(frame.Definition);
            else
                OpenDropdown(frame.Definition, target.ItemIndex);
            return UiInputResult.HandledAndInvalidate;
        }
        if (!IsLeftMouseDown(mouse))
            return UiInputResult.HandledResult;
        if (target is null) { Close(); return UiInputResult.HandledAndInvalidate; }
        if (target.Kind == TopMenuPointerTargetKind.Surface)
            return UiInputResult.HandledResult;
        if (target.Kind == TopMenuPointerTargetKind.Top)
            OpenDropdown(frame.Definition, target.ItemIndex);
        else if (target.Kind == TopMenuPointerTargetKind.DropdownItem)
            ExecuteDropdownItem(frame.Definition, target.ItemIndex);
        return UiInputResult.HandledAndInvalidate;
    }

    private UiInputResult RouteScrollbar(MouseConsoleInputEvent mouse, TopMenuFrame frame, bool captured)
    {
        if (frame.DropdownScrollbar is not { } scrollbar)
            return UiInputResult.NotHandled;
        IReadOnlyList<MenuItemDefinition> children = CurrentChildren(frame.Definition);
        VerticalScrollbarInputResult result = _dropdownScrollbar.HandleMouse(mouse, scrollbar);
        if (!result.IsHandled)
            return UiInputResult.NotHandled;
        int last = Math.Min(children.Count - 1, result.FirstVisibleIndex + scrollbar.ViewportItems - 1);
        _state.ActiveDropdownItemIndex = SelectableIndexInRange(children, _state.ActiveDropdownItemIndex, result.FirstVisibleIndex, last);
        _state.DropdownFirstVisibleItemIndex = result.FirstVisibleIndex;
        if (mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down)
            return UiInputResult.CaptureMouse(ScrollbarTarget, MouseButton.Left, true);
        if (captured && mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Up)
            return UiInputResult.ReleaseMouse(true);
        return UiInputResult.HandledAndInvalidate;
    }

    private void OpenInitial(MenuBarDefinition definition)
    {
        OpenDropdown(definition, FindTopIndex(definition, _getInitialTopItemId()));
    }

    private void OpenDropdown(MenuBarDefinition definition, int topIndex)
    {
        if (definition.Items.Count == 0) { Close(); return; }
        if (!IsOpen)
            _opening();
        _state.ActiveTopMenuIndex = Math.Clamp(topIndex, 0, definition.Items.Count - 1);
        _state.OpenState = MenuOpenState.DropdownOpen;
        _state.ActiveDropdownItemIndex = FirstSelectableIndex(CurrentChildren(definition));
        _state.DropdownFirstVisibleItemIndex = 0;
        _dropdownScrollbar.ApplyCommittedFrame(null);
    }

    private void MoveTop(MenuBarDefinition definition, int delta)
    {
        if (definition.Items.Count == 0) return;
        _state.ActiveTopMenuIndex = ((_state.ActiveTopMenuIndex + delta) % definition.Items.Count + definition.Items.Count) % definition.Items.Count;
        if (IsOpen) _state.ActiveDropdownItemIndex = FirstSelectableIndex(CurrentChildren(definition));
    }

    private void MoveDropdown(MenuBarDefinition definition, int delta)
    {
        if (!IsOpen) { OpenDropdown(definition, _state.ActiveTopMenuIndex); return; }
        IReadOnlyList<MenuItemDefinition> items = CurrentChildren(definition);
        if (items.Count == 0) return;
        int start = _state.ActiveDropdownItemIndex;
        if (start < 0 || start >= items.Count || !IsSelectable(items[start])) start = delta >= 0 ? -1 : items.Count;
        for (int step = 1; step <= items.Count; step++)
        {
            int index = ((start + delta * step) % items.Count + items.Count) % items.Count;
            if (IsSelectable(items[index])) { _state.ActiveDropdownItemIndex = index; return; }
        }
    }

    private void SelectBoundary(MenuBarDefinition definition, bool first)
    {
        if (!IsOpen) OpenDropdown(definition, _state.ActiveTopMenuIndex);
        _state.ActiveDropdownItemIndex = first ? FirstSelectableIndex(CurrentChildren(definition)) : LastSelectableIndex(CurrentChildren(definition));
    }

    private void ExecuteActive(MenuBarDefinition definition)
    {
        if (!IsOpen) { OpenDropdown(definition, _state.ActiveTopMenuIndex); return; }
        ExecuteDropdownItem(definition, _state.ActiveDropdownItemIndex);
    }

    private void ExecuteDropdownItem(MenuBarDefinition definition, int itemIndex)
    {
        IReadOnlyList<MenuItemDefinition> children = CurrentChildren(definition);
        if (itemIndex < 0 || itemIndex >= children.Count || !IsSelectable(children[itemIndex]) || children[itemIndex].CommandId is not { } commandId) return;
        object? args = children[itemIndex].CommandArgs;
        Close();
        _executeCommand(new MenuCommandRequest { CommandId = commandId, Args = args });
    }

    private bool TryHandleHotKey(char key, MenuBarDefinition definition)
    {
        if (key == '\0') return false;
        if (IsOpen)
            for (int i = 0; i < CurrentChildren(definition).Count; i++)
                if (IsSelectable(CurrentChildren(definition)[i]) && MatchesHotChar(CurrentChildren(definition)[i].HotChar, key)) { ExecuteDropdownItem(definition, i); return true; }
        for (int i = 0; i < definition.Items.Count; i++)
            if (MatchesHotChar(definition.Items[i].HotChar, key)) { OpenDropdown(definition, i); return true; }
        return false;
    }

    private VerticalScrollbarFrame? CalculateScrollbar(MenuBarDefinition definition, MenuLayout layout)
    {
        if (!IsOpen || layout.DropdownBounds is not { } dropdown || _state.ActiveTopMenuIndex < 0 || _state.ActiveTopMenuIndex >= definition.Items.Count) return null;
        int rows = Math.Max(0, dropdown.Height - 2);
        return _dropdownScrollbar.CalculateFrame(new Rect(dropdown.Right - 1, dropdown.Y + 1, 1, rows), new ScrollState { TotalItems = definition.Items[_state.ActiveTopMenuIndex].Children.Count, ViewportItems = rows, FirstVisibleIndex = layout.DropdownFirstVisibleItemIndex });
    }

    private IReadOnlyList<TopMenuPointerTarget> BuildPointerTargets(MenuBarDefinition definition, MenuLayout layout, Rect activationBounds, Rect? scrollbar)
    {
        var result = new List<TopMenuPointerTarget>();
        if (!IsOpen)
        {
            result.Add(new(ActivationTarget, activationBounds, TopMenuPointerTargetKind.Activation));
            for (int i = 0; i < layout.TopItemBounds.Count && i < definition.Items.Count; i++) result.Add(new(TopTarget(definition.Items[i].Id), layout.TopItemBounds[i], TopMenuPointerTargetKind.Top, i));
            return result;
        }
        for (int i = 0; i < layout.TopItemBounds.Count && i < definition.Items.Count; i++) result.Add(new(TopTarget(definition.Items[i].Id), layout.TopItemBounds[i], TopMenuPointerTargetKind.Top, i));
        if (layout.DropdownBounds is not { } dropdown || _state.ActiveTopMenuIndex < 0 || _state.ActiveTopMenuIndex >= definition.Items.Count) return result;
        result.Add(new(new UiTargetId("top-menu.surface"), dropdown, TopMenuPointerTargetKind.Surface));
        int rows = Math.Max(0, dropdown.Height - 2);
        IReadOnlyList<MenuItemDefinition> children = definition.Items[_state.ActiveTopMenuIndex].Children;
        for (int row = 0; row < rows; row++)
        {
            int item = layout.DropdownFirstVisibleItemIndex + row;
            if (item >= children.Count) break;
            result.Add(new(DropdownTarget(definition.Items[_state.ActiveTopMenuIndex].Id, item), new Rect(dropdown.X + 1, dropdown.Y + 1 + row, Math.Max(0, dropdown.Width - 2), 1), TopMenuPointerTargetKind.DropdownItem, item));
        }
        if (scrollbar is { } bounds) result.Add(new(ScrollbarTarget, bounds, TopMenuPointerTargetKind.Scrollbar));
        return result;
    }

    private UiTargetId ActiveTarget(TopMenuFrame frame)
    {
        if (_state.ActiveTopMenuIndex >= 0 && _state.ActiveTopMenuIndex < frame.Definition.Items.Count)
        {
            TopMenuItemDefinition top = frame.Definition.Items[_state.ActiveTopMenuIndex];
            if (_state.ActiveDropdownItemIndex >= 0 && _state.ActiveDropdownItemIndex < top.Children.Count && IsSelectable(top.Children[_state.ActiveDropdownItemIndex])) return DropdownTarget(top.Id, _state.ActiveDropdownItemIndex);
            return TopTarget(top.Id);
        }
        return ActivationTarget;
    }

    private TopMenuItemDefinition? CurrentTopItem(MenuBarDefinition definition) => _state.ActiveTopMenuIndex >= 0 && _state.ActiveTopMenuIndex < definition.Items.Count ? definition.Items[_state.ActiveTopMenuIndex] : null;
    private IReadOnlyList<MenuItemDefinition> CurrentChildren(MenuBarDefinition definition) => CurrentTopItem(definition)?.Children ?? [];
    private static TopMenuPointerTarget? FindTarget(TopMenuFrame frame, UiTargetId? id) => id is null ? null : frame.PointerTargets.FirstOrDefault(target => target.Target == id);
    private static int FindTopIndex(MenuBarDefinition definition, string? id) => definition.Items.ToList().FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0 ? index : 0;
    private static int FirstSelectableIndex(IReadOnlyList<MenuItemDefinition> items) => Enumerable.Range(0, items.Count).FirstOrDefault(index => IsSelectable(items[index]), -1);
    private static int LastSelectableIndex(IReadOnlyList<MenuItemDefinition> items) => Enumerable.Range(0, items.Count).Reverse().FirstOrDefault(index => IsSelectable(items[index]), -1);
    private static int SelectableIndexInRange(IReadOnlyList<MenuItemDefinition> items, int preferred, int first, int last) => preferred >= first && preferred <= last && IsSelectable(items[preferred]) ? preferred : Enumerable.Range(first, Math.Max(0, last - first + 1)).FirstOrDefault(index => IsSelectable(items[index]), preferred);
    private static bool IsSelectable(MenuItemDefinition item) => item.Kind != MenuItemKind.Separator && item.IsEnabled;
    private static bool MatchesHotChar(char? hotChar, char key) => hotChar.HasValue && char.ToUpperInvariant(hotChar.Value) == char.ToUpperInvariant(key);
    private static bool IsPlainKey(ConsoleKeyInfo key, ConsoleKey expected) => key.Key == expected && key.Modifiers == 0;
    private static bool IsLeftMouseDown(MouseConsoleInputEvent mouse) => mouse.Button == MouseButton.Left && mouse.Kind == MouseEventKind.Down;
    private static UiTargetId TopTarget(string id) => new($"top-menu.top:{id}");
    private static UiTargetId DropdownTarget(string topId, int itemIndex) => new($"top-menu.dropdown:{topId}:{itemIndex}");
}

public sealed record TopMenuFrame(bool Available, bool Open, MenuBarDefinition Definition, MenuLayout Layout, Rect ActivationBounds, VerticalScrollbarFrame? DropdownScrollbar, IReadOnlyList<TopMenuPointerTarget> PointerTargets)
{
    internal static TopMenuFrame Unavailable(ConsoleViewport viewport) => new(false, false, new MenuBarDefinition { Items = [] }, new MenuLayout { TopItemBounds = [], DropdownBounds = null, DropdownFirstVisibleItemIndex = -1 }, default, null, []);
}

public sealed record TopMenuPointerTarget(UiTargetId Target, Rect Bounds, TopMenuPointerTargetKind Kind, int ItemIndex = -1);
public enum TopMenuPointerTargetKind { Activation, Top, DropdownItem, Surface, Scrollbar }
