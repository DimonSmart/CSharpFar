using CSharpFar.App.Menu;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;

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

        var popupOptions = new PopupRenderOptions
        {
            BorderStyle = options.BorderStyle,
            BackgroundStyle = options.NormalStyle,
            ShadowStyle = options.ShadowStyle,
            DrawBorder = true,
            DrawShadow = true,
        };

        var children = definition.Items[state.ActiveTopMenuIndex].Children;
        _popupRenderer.RenderPopup(screen, bounds, popupOptions, (_, contentBounds) =>
            RenderItems(screen, contentBounds, children, state.ActiveDropdownItemIndex, options));
    }

    private static void RenderItems(
        ScreenRenderer screen,
        Rect contentBounds,
        IReadOnlyList<MenuItemDefinition> items,
        int activeIndex,
        MenuRenderOptions options)
    {
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        int rows = Math.Min(contentBounds.Height, items.Count);
        for (int i = 0; i < rows; i++)
        {
            var item = items[i];
            int y = contentBounds.Y + i;

            if (item.Kind == MenuItemKind.Separator)
            {
                screen.Write(contentBounds.X, y, new string('─', contentBounds.Width), options.BorderStyle);
                continue;
            }

            var style = !item.IsEnabled
                ? options.DisabledStyle
                : i == activeIndex
                    ? options.ActiveStyle
                    : options.NormalStyle;

            string text = FormatItem(item);
            if (text.Length > contentBounds.Width)
                text = text[..contentBounds.Width];
            else
                text = text.PadRight(contentBounds.Width);

            screen.Write(contentBounds.X, y, text, style);
        }
    }

    private static string FormatItem(MenuItemDefinition item) =>
        MenuLayoutService.DropdownPrefix(item) + item.Text;
}
