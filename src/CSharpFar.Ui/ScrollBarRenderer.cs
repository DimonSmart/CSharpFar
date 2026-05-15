using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ScrollBarRenderer
{
    public void RenderVerticalScrollbar(
        ScreenRenderer      screen,
        Rect                bounds,
        ScrollState         state,
        ScrollBarOptions    options,
        CellStyle           style)
    {
        if (!options.Enabled) return;
        if (bounds.Height < 3) return;

        bool scrollable = state.TotalItems > state.ViewportItems;
        if (!scrollable && !options.DrawWhenNotScrollable) return;

        var thumb = ScrollBarInteraction.CalculateThumb(bounds, state);

        screen.WriteChar(bounds.X, bounds.Y,          '▲', style);
        screen.WriteChar(bounds.X, bounds.Bottom - 1, '▼', style);

        for (int i = 0; i < thumb.TrackHeight; i++)
            screen.WriteChar(bounds.X, bounds.Y + 1 + i, '░', style);

        if (scrollable && thumb.TrackHeight > 0)
        {
            for (int i = 0; i < thumb.ThumbHeight; i++)
                screen.WriteChar(bounds.X, thumb.ThumbY + i, '█', style);
        }
    }
}
