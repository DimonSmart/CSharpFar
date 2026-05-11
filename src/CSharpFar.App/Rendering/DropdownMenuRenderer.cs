using CSharpFar.App.Menu;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

public sealed class DropdownMenuRenderer
{
    private readonly PopupRenderer _popupRenderer = new();

    public void Render(
        ScreenRenderer screen,
        MenuBarDefinition definition,
        MenuState state,
        MenuLayout layout,
        MenuRenderOptions options)
    {
        if (state.OpenState != MenuOpenState.DropdownOpen ||
            layout.DropdownBounds is not { } bounds ||
            state.ActiveTopMenuIndex < 0 ||
            state.ActiveTopMenuIndex >= definition.Items.Count)
        {
            return;
        }

        var children = definition.Items[state.ActiveTopMenuIndex].Children;
        int visibleRows = Math.Max(0, bounds.Height - 2);
        var popupOptions = new PopupRenderOptions
        {
            BorderStyle = options.BorderStyle,
            BackgroundStyle = options.NormalStyle,
            ShadowStyle = options.ShadowStyle,
            DrawBorder = true,
            DrawShadow = true,
            VerticalScrollState = children.Count > visibleRows
                ? new ScrollState
                {
                    TotalItems = children.Count,
                    ViewportItems = visibleRows,
                    FirstVisibleIndex = layout.DropdownFirstVisibleItemIndex,
                }
                : null,
        };

        _popupRenderer.RenderPopup(screen, bounds, popupOptions, (_, contentBounds) =>
            RenderItems(
                screen,
                contentBounds,
                children,
                state.ActiveDropdownItemIndex,
                layout.DropdownFirstVisibleItemIndex,
                options));
    }

    private static void RenderItems(
        ScreenRenderer screen,
        Rect contentBounds,
        IReadOnlyList<MenuItemDefinition> items,
        int activeIndex,
        int firstVisibleItemIndex,
        MenuRenderOptions options)
    {
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        int rows = Math.Min(contentBounds.Height, items.Count - firstVisibleItemIndex);
        for (int i = 0; i < rows; i++)
        {
            int itemIndex = firstVisibleItemIndex + i;
            var item = items[itemIndex];
            int y = contentBounds.Y + i;

            if (item.Kind == MenuItemKind.Separator)
            {
                screen.Write(contentBounds.X, y, new string('─', contentBounds.Width), options.BorderStyle);
                continue;
            }

            var style = !item.IsEnabled
                ? options.DisabledStyle
                : itemIndex == activeIndex
                    ? options.ActiveStyle
                    : options.NormalStyle;

            string text = FormatItem(item);
            int hotKeyIndex = FindHotKeyIndex(item);
            if (text.Length > contentBounds.Width)
                text = text[..contentBounds.Width];
            else
                text = text.PadRight(contentBounds.Width);

            if (!item.IsEnabled || hotKeyIndex < 0 || hotKeyIndex >= text.Length)
            {
                screen.Write(contentBounds.X, y, text, style);
                continue;
            }

            var highlightStyle = itemIndex == activeIndex
                ? options.ActiveHighlightStyle
                : options.HighlightStyle;
            WriteWithHotKey(screen, contentBounds.X, y, text, hotKeyIndex, style, highlightStyle);
        }
    }

    private static string FormatItem(MenuItemDefinition item) =>
        MenuLayoutService.DropdownPrefix(item) + item.Text;

    private static int FindHotKeyIndex(MenuItemDefinition item)
    {
        if (!item.HotKey.HasValue)
            return -1;

        int index = item.Text.IndexOf(item.HotKey.Value, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? -1 : MenuLayoutService.DropdownPrefix(item).Length + index;
    }

    private static void WriteWithHotKey(
        ScreenRenderer screen,
        int x,
        int y,
        string text,
        int hotKeyIndex,
        CellStyle style,
        CellStyle highlightStyle)
    {
        if (hotKeyIndex > 0)
            screen.Write(x, y, text[..hotKeyIndex], style);

        screen.Write(x + hotKeyIndex, y, text.AsSpan(hotKeyIndex, 1), highlightStyle);

        int tailStart = hotKeyIndex + 1;
        if (tailStart < text.Length)
            screen.Write(x + tailStart, y, text[tailStart..], style);
    }
}
