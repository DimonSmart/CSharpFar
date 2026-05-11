using CSharpFar.Core.Menu;

namespace CSharpFar.App.Menu;

public sealed class MenuHitTester
{
    public MenuHitTestResult HitTest(
        int x,
        int y,
        MenuBarDefinition definition,
        MenuState state,
        MenuLayout layout)
    {
        for (int i = 0; i < layout.TopItemBounds.Count; i++)
        {
            if (layout.TopItemBounds[i].Contains(x, y))
                return new MenuHitTestResult
                {
                    Kind = MenuHitTestKind.TopMenuItem,
                    TopMenuIndex = i,
                };
        }

        if (state.OpenState == MenuOpenState.Closed)
            return new MenuHitTestResult { Kind = MenuHitTestKind.None };

        if (layout.DropdownBounds is { } dropdown &&
            state.ActiveTopMenuIndex >= 0 &&
            state.ActiveTopMenuIndex < definition.Items.Count &&
            dropdown.Contains(x, y))
        {
            bool onBorder = x == dropdown.X ||
                            x == dropdown.Right - 1 ||
                            y == dropdown.Y ||
                            y == dropdown.Bottom - 1;
            if (onBorder)
                return new MenuHitTestResult { Kind = MenuHitTestKind.DropdownBorder };

            int itemIndex = layout.DropdownFirstVisibleItemIndex + y - dropdown.Y - 1;
            if (itemIndex >= 0 &&
                itemIndex < definition.Items[state.ActiveTopMenuIndex].Children.Count)
            {
                return new MenuHitTestResult
                {
                    Kind = MenuHitTestKind.DropdownItem,
                    DropdownItemIndex = itemIndex,
                };
            }

            return new MenuHitTestResult { Kind = MenuHitTestKind.DropdownBorder };
        }

        return new MenuHitTestResult { Kind = MenuHitTestKind.Outside };
    }
}
