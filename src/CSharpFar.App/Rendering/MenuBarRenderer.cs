using CSharpFar.App.Menu;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Rendering;

public sealed class MenuBarRenderer
{
    public void Render(
        IUiCanvas screen,
        Rect screenBounds,
        MenuBarDefinition definition,
        MenuState state,
        MenuLayout layout,
        MenuRenderOptions options)
    {
        if (state.OpenState == MenuOpenState.Closed)
            return;

        screen.FillRegion(new Rect(screenBounds.X, screenBounds.Y, screenBounds.Width, 1), options.MenuBarNormalStyle);

        for (int i = 0; i < definition.Items.Count && i < layout.TopItemBounds.Count; i++)
        {
            var bounds = layout.TopItemBounds[i];
            var style = i == state.ActiveTopMenuIndex
                ? options.MenuBarActiveStyle
                : options.MenuBarNormalStyle;
            string text = $" {definition.Items[i].Text} ";
            var highlightStyle = i == state.ActiveTopMenuIndex
                ? options.ActiveHighlightStyle
                : options.HighlightStyle;

            WriteWithHotKey(screen, bounds.X, bounds.Y, text, definition.Items[i].HotChar, style, highlightStyle);
        }
    }

    private static void WriteWithHotKey(
        IUiCanvas screen,
        int x,
        int y,
        string text,
        char? hotKey,
        CellStyle style,
        CellStyle highlightStyle)
    {
        int hotKeyIndex = FindHotKeyIndex(text, hotKey);
        if (hotKeyIndex < 0)
        {
            screen.Write(x, y, text, style);
            return;
        }

        if (hotKeyIndex > 0)
            screen.Write(x, y, text[..hotKeyIndex], style);

        screen.Write(x + hotKeyIndex, y, text.AsSpan(hotKeyIndex, 1), highlightStyle);

        int tailStart = hotKeyIndex + 1;
        if (tailStart < text.Length)
            screen.Write(x + tailStart, y, text[tailStart..], style);
    }

    private static int FindHotKeyIndex(string text, char? hotKey) =>
        hotKey.HasValue
            ? text.IndexOf(hotKey.Value, StringComparison.OrdinalIgnoreCase)
            : -1;
}
