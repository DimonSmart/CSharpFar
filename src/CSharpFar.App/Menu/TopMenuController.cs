using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Menu;

public sealed class TopMenuController
{
    private readonly MenuState _state;
    private readonly Func<MenuCommandRequest, MenuCommandResult> _executeCommand;
    private readonly MenuHitTester _hitTester = new();
    private ScrollBarDragState? _dropdownScrollbarDrag;

    public TopMenuController(
        MenuState state,
        Func<MenuCommandRequest, MenuCommandResult> executeCommand)
    {
        _state = state;
        _executeCommand = executeCommand;
    }

    public bool HandleKey(ConsoleKeyInfo key, MenuBarDefinition definition, PanelSide activePanelSide)
    {
        if (_state.OpenState == MenuOpenState.Closed)
        {
            if (IsPlainKey(key, ConsoleKey.F9))
            {
                OpenForPanel(definition, activePanelSide);
                return true;
            }

            return false;
        }

        if (IsPlainKey(key, ConsoleKey.F9))
        {
            OpenForPanel(definition, activePanelSide);
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                Close();
                return true;
            case ConsoleKey.LeftArrow:
                MoveTop(definition, -1);
                return true;
            case ConsoleKey.RightArrow:
                MoveTop(definition, +1);
                return true;
            case ConsoleKey.DownArrow:
                MoveDropdown(definition, +1, openIfNeeded: true);
                return true;
            case ConsoleKey.UpArrow:
                MoveDropdown(definition, -1, openIfNeeded: true);
                return true;
            case ConsoleKey.Home:
                SelectDropdownBoundary(definition, first: true);
                return true;
            case ConsoleKey.End:
                SelectDropdownBoundary(definition, first: false);
                return true;
            case ConsoleKey.Enter:
                ExecuteActiveDropdownItem(definition);
                return true;
            case ConsoleKey.Tab:
                SwitchPanelMenu(definition, activePanelSide);
                return true;
        }

        if (TryHandleHotKey(key.KeyChar, definition))
            return true;

        return true;
    }

    public bool HandleMouse(
        MouseConsoleInputEvent mouse,
        MenuBarDefinition definition,
        MenuLayout layout,
        PanelSide activePanelSide)
    {
        if (!IsMenuMousePress(mouse))
        {
            return TryHandleDropdownScrollbarMouse(mouse, definition, layout) ||
                   _state.OpenState != MenuOpenState.Closed;
        }

        if (TryHandleDropdownScrollbarMouse(mouse, definition, layout))
            return true;

        var hit = _hitTester.HitTest(mouse.X, mouse.Y, definition, _state, layout);

        if (_state.OpenState == MenuOpenState.Closed)
        {
            if (mouse.Y != 0)
                return false;

            int topIndex = hit.Kind == MenuHitTestKind.TopMenuItem
                ? hit.TopMenuIndex.GetValueOrDefault()
                : TopIndexForPanel(definition, activePanelSide);
            OpenDropdown(definition, topIndex);
            return true;
        }

        switch (hit.Kind)
        {
            case MenuHitTestKind.TopMenuItem:
                OpenDropdown(definition, hit.TopMenuIndex.GetValueOrDefault());
                return true;
            case MenuHitTestKind.DropdownItem:
                ExecuteDropdownItem(definition, hit.DropdownItemIndex.GetValueOrDefault());
                return true;
            case MenuHitTestKind.DropdownBorder:
                return true;
            case MenuHitTestKind.Outside:
                Close();
                return true;
            default:
                return true;
        }
    }

    public void Close()
    {
        _state.OpenState = MenuOpenState.Closed;
        _state.ActiveDropdownItemIndex = 0;
        _dropdownScrollbarDrag = null;
    }

    private void OpenForPanel(MenuBarDefinition definition, PanelSide panelSide)
    {
        OpenDropdown(definition, TopIndexForPanel(definition, panelSide));
    }

    private void OpenDropdown(MenuBarDefinition definition, int topIndex)
    {
        if (definition.Items.Count == 0)
        {
            Close();
            return;
        }

        _state.ActiveTopMenuIndex = Math.Clamp(topIndex, 0, definition.Items.Count - 1);
        _state.OpenState = MenuOpenState.DropdownOpen;
        _state.ActiveDropdownItemIndex = FirstSelectableIndex(CurrentChildren(definition));
        _dropdownScrollbarDrag = null;
    }

    private bool TryHandleDropdownScrollbarMouse(
        MouseConsoleInputEvent mouse,
        MenuBarDefinition definition,
        MenuLayout layout)
    {
        if (_state.OpenState != MenuOpenState.DropdownOpen ||
            layout.DropdownBounds is not { } dropdown ||
            _state.ActiveTopMenuIndex < 0 ||
            _state.ActiveTopMenuIndex >= definition.Items.Count)
        {
            return false;
        }

        var children = definition.Items[_state.ActiveTopMenuIndex].Children;
        int visibleRows = Math.Max(0, dropdown.Height - 2);
        if (children.Count <= visibleRows || visibleRows <= 0)
            return false;

        int selectedIndex = _state.ActiveDropdownItemIndex;
        int firstVisibleIndex = layout.DropdownFirstVisibleItemIndex;
        var scrollbarBounds = new Rect(dropdown.Right - 1, dropdown.Y + 1, 1, visibleRows);

        if (!ScrollableListMouseHandler.TryHandleScrollbarMouse(
                mouse,
                scrollbarBounds,
                children.Count,
                visibleRows,
                ref selectedIndex,
                ref firstVisibleIndex,
                ref _dropdownScrollbarDrag))
        {
            return false;
        }

        int lastVisibleIndex = Math.Min(children.Count - 1, firstVisibleIndex + visibleRows - 1);
        _state.ActiveDropdownItemIndex = SelectableIndexInRange(children, selectedIndex, firstVisibleIndex, lastVisibleIndex);
        return true;
    }

    private static int SelectableIndexInRange(
        IReadOnlyList<MenuItemDefinition> items,
        int preferredIndex,
        int firstIndex,
        int lastIndex)
    {
        if (preferredIndex >= firstIndex &&
            preferredIndex <= lastIndex &&
            IsSelectable(items[preferredIndex]))
        {
            return preferredIndex;
        }

        for (int i = firstIndex; i <= lastIndex; i++)
            if (IsSelectable(items[i]))
                return i;

        return preferredIndex;
    }

    private void MoveTop(MenuBarDefinition definition, int delta)
    {
        if (definition.Items.Count == 0)
            return;

        int count = definition.Items.Count;
        _state.ActiveTopMenuIndex = ((_state.ActiveTopMenuIndex + delta) % count + count) % count;
        if (_state.OpenState == MenuOpenState.DropdownOpen)
            _state.ActiveDropdownItemIndex = FirstSelectableIndex(CurrentChildren(definition));
    }

    private void MoveDropdown(MenuBarDefinition definition, int delta, bool openIfNeeded)
    {
        if (_state.OpenState != MenuOpenState.DropdownOpen)
        {
            if (openIfNeeded)
                OpenDropdown(definition, _state.ActiveTopMenuIndex);
            return;
        }

        var children = CurrentChildren(definition);
        if (children.Count == 0)
            return;

        int start = _state.ActiveDropdownItemIndex;
        if (start < 0 || start >= children.Count || !IsSelectable(children[start]))
            start = delta >= 0 ? -1 : children.Count;

        for (int step = 1; step <= children.Count; step++)
        {
            int index = ((start + delta * step) % children.Count + children.Count) % children.Count;
            if (IsSelectable(children[index]))
            {
                _state.ActiveDropdownItemIndex = index;
                return;
            }
        }
    }

    private void SelectDropdownBoundary(MenuBarDefinition definition, bool first)
    {
        if (_state.OpenState != MenuOpenState.DropdownOpen)
            OpenDropdown(definition, _state.ActiveTopMenuIndex);

        var children = CurrentChildren(definition);
        _state.ActiveDropdownItemIndex = first
            ? FirstSelectableIndex(children)
            : LastSelectableIndex(children);
    }

    private void ExecuteActiveDropdownItem(MenuBarDefinition definition)
    {
        if (_state.OpenState != MenuOpenState.DropdownOpen)
        {
            OpenDropdown(definition, _state.ActiveTopMenuIndex);
            return;
        }

        ExecuteDropdownItem(definition, _state.ActiveDropdownItemIndex);
    }

    private void ExecuteDropdownItem(MenuBarDefinition definition, int itemIndex)
    {
        var children = CurrentChildren(definition);
        if (itemIndex < 0 || itemIndex >= children.Count)
            return;

        var item = children[itemIndex];
        if (!IsSelectable(item) || item.CommandId is null)
            return;

        var request = new MenuCommandRequest
        {
            CommandId = item.CommandId,
            Args = item.CommandArgs,
        };
        Close();
        _executeCommand(request);
    }

    private bool TryHandleHotKey(char keyChar, MenuBarDefinition definition)
    {
        if (keyChar == '\0')
            return false;

        if (_state.OpenState == MenuOpenState.DropdownOpen)
        {
            var children = CurrentChildren(definition);
            for (int i = 0; i < children.Count; i++)
            {
                if (IsSelectable(children[i]) && MatchesHotKey(children[i].HotKey, keyChar))
                {
                    ExecuteDropdownItem(definition, i);
                    return true;
                }
            }
        }

        for (int i = 0; i < definition.Items.Count; i++)
        {
            if (MatchesHotKey(definition.Items[i].HotKey, keyChar))
            {
                OpenDropdown(definition, i);
                return true;
            }
        }

        return false;
    }

    private void SwitchPanelMenu(MenuBarDefinition definition, PanelSide activePanelSide)
    {
        var current = CurrentTopItem(definition);
        if (current?.Id == "Left")
        {
            OpenDropdown(definition, FindTopIndex(definition, "Right"));
            return;
        }

        if (current?.Id == "Right")
        {
            OpenDropdown(definition, FindTopIndex(definition, "Left"));
            return;
        }

        var passive = activePanelSide == PanelSide.Left ? "Right" : "Left";
        OpenDropdown(definition, FindTopIndex(definition, passive));
    }

    private TopMenuItemDefinition? CurrentTopItem(MenuBarDefinition definition) =>
        _state.ActiveTopMenuIndex >= 0 && _state.ActiveTopMenuIndex < definition.Items.Count
            ? definition.Items[_state.ActiveTopMenuIndex]
            : null;

    private IReadOnlyList<MenuItemDefinition> CurrentChildren(MenuBarDefinition definition) =>
        CurrentTopItem(definition)?.Children ?? [];

    private static int TopIndexForPanel(MenuBarDefinition definition, PanelSide panelSide) =>
        FindTopIndex(definition, panelSide == PanelSide.Left ? "Left" : "Right");

    private static int FindTopIndex(MenuBarDefinition definition, string id)
    {
        for (int i = 0; i < definition.Items.Count; i++)
            if (string.Equals(definition.Items[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;

        for (int i = 0; i < definition.Items.Count; i++)
            if (string.Equals(definition.Items[i].Text, id, StringComparison.OrdinalIgnoreCase))
                return i;

        return Math.Min(2, Math.Max(0, definition.Items.Count - 1));
    }

    private static int FirstSelectableIndex(IReadOnlyList<MenuItemDefinition> items)
    {
        for (int i = 0; i < items.Count; i++)
            if (IsSelectable(items[i]))
                return i;
        return -1;
    }

    private static int LastSelectableIndex(IReadOnlyList<MenuItemDefinition> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
            if (IsSelectable(items[i]))
                return i;
        return -1;
    }

    private static bool IsSelectable(MenuItemDefinition item) =>
        item.Kind != MenuItemKind.Separator && item.IsEnabled;

    private static bool MatchesHotKey(char? hotKey, char keyChar) =>
        hotKey.HasValue &&
        char.ToUpperInvariant(hotKey.Value) == char.ToUpperInvariant(keyChar);

    private static bool IsPlainKey(ConsoleKeyInfo key, ConsoleKey consoleKey) =>
        key.Key == consoleKey && key.Modifiers == 0;

    private static bool IsMenuMousePress(MouseConsoleInputEvent mouse) =>
        mouse.Button == MouseButton.Left &&
        (mouse.Kind == MouseEventKind.Down || mouse.Kind == MouseEventKind.Click);
}
