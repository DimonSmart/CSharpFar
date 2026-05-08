using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Menu;

public sealed class MenuLayoutService
{
    private const int TopItemPadding = 1;
    private const int TopItemSpacing = 3;

    public MenuLayout CalculateLayout(
        Rect screenBounds,
        MenuBarDefinition definition,
        MenuState state)
    {
        var topBounds = new List<Rect>(definition.Items.Count);
        int x = screenBounds.X;
        foreach (var item in definition.Items)
        {
            int width = item.Text.Length + TopItemPadding * 2;
            topBounds.Add(new Rect(x, screenBounds.Y, width, 1));
            x += width + TopItemSpacing;
        }

        Rect? dropdownBounds = null;
        if (state.OpenState == MenuOpenState.DropdownOpen &&
            state.ActiveTopMenuIndex >= 0 &&
            state.ActiveTopMenuIndex < definition.Items.Count)
        {
            var topItem = definition.Items[state.ActiveTopMenuIndex];
            if (topItem.Children.Count > 0)
            {
                int contentWidth = topItem.Children
                    .Select(DropdownTextLength)
                    .DefaultIfEmpty(0)
                    .Max();
                int width = Math.Max(2, contentWidth + 2);
                if (screenBounds.Width > 0)
                    width = Math.Min(width, screenBounds.Width);
                int height = Math.Max(2, topItem.Children.Count + 2);

                int dropdownX = topBounds[state.ActiveTopMenuIndex].X;
                if (dropdownX + width > screenBounds.Right)
                    dropdownX = screenBounds.Right - width;
                if (dropdownX < screenBounds.X)
                    dropdownX = screenBounds.X;

                dropdownBounds = new Rect(dropdownX, screenBounds.Y + 1, width, height);
            }
        }

        return new MenuLayout
        {
            TopItemBounds = topBounds,
            DropdownBounds = dropdownBounds,
        };
    }

    internal static int DropdownTextLength(MenuItemDefinition item) =>
        item.Kind == MenuItemKind.Separator
            ? 1
            : DropdownPrefix(item).Length + item.Text.Length;

    internal static string DropdownPrefix(MenuItemDefinition item) => item.Kind switch
    {
        MenuItemKind.CheckBox => item.IsChecked ? "[x] " : "[ ] ",
        MenuItemKind.Radio => item.IsChecked ? "(*) " : "( ) ",
        _ => "    ",
    };
}
