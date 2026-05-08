using CSharpFar.App.Menu;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Rendering;

public sealed class MenuBarRenderer
{
    public void Render(
        ScreenRenderer screen,
        Rect screenBounds,
        MenuBarDefinition definition,
        MenuState state,
        MenuLayout layout,
        MenuRenderOptions options)
    {
        if (state.OpenState == MenuOpenState.Closed)
            return;

        screen.FillRegion(new Rect(screenBounds.X, screenBounds.Y, screenBounds.Width, 1), options.NormalStyle);

        for (int i = 0; i < definition.Items.Count && i < layout.TopItemBounds.Count; i++)
        {
            var bounds = layout.TopItemBounds[i];
            var style = i == state.ActiveTopMenuIndex
                ? options.ActiveStyle
                : options.NormalStyle;
            string text = $" {definition.Items[i].Text} ";
            screen.Write(bounds.X, bounds.Y, text, style);
        }
    }
}
