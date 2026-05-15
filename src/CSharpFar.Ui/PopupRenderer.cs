using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class PopupRenderer
{
    public void RenderPopup(
        ScreenRenderer screen,
        Rect bounds,
        PopupRenderOptions options,
        Action<ScreenRenderer, Rect> renderContent)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (options.DrawShadow)
        {
            var shadow = new Rect(
                bounds.X + options.ShadowOffsetX,
                bounds.Y + options.ShadowOffsetY,
                bounds.Width,
                bounds.Height);
            screen.FillRegion(shadow, options.ShadowStyle);
        }

        screen.FillRegion(bounds, options.BackgroundStyle);

        Rect contentBounds = bounds;
        if (options.DrawBorder)
        {
            if (options.DrawDoubleBorder)
                screen.DrawDoubleBox(bounds, options.BorderStyle);
            else
                screen.DrawBox(bounds, options.BorderStyle);

            contentBounds = new Rect(
                bounds.X + 1,
                bounds.Y + 1,
                Math.Max(0, bounds.Width - 2),
                Math.Max(0, bounds.Height - 2));
        }

        renderContent(screen, contentBounds);

        if (options.DrawBorder && options.VerticalScrollState is { } scrollState)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                screen,
                new Rect(bounds.Right - 1, contentBounds.Y, 1, contentBounds.Height),
                scrollState,
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                options.BorderStyle);
        }
    }
}
