using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

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

        int trackHeight = bounds.Height - 2;

        screen.WriteChar(bounds.X, bounds.Y,          '▲', style);
        screen.WriteChar(bounds.X, bounds.Bottom - 1, '▼', style);

        for (int i = 0; i < trackHeight; i++)
            screen.WriteChar(bounds.X, bounds.Y + 1 + i, '░', style);

        if (scrollable && trackHeight > 0)
        {
            int thumbHeight = Math.Max(1, (int)Math.Round(
                (double)state.ViewportItems * trackHeight / state.TotalItems));
            int thumbOffset = (int)Math.Round(
                (double)state.FirstVisibleIndex * trackHeight / state.TotalItems);
            thumbOffset = Math.Min(thumbOffset, trackHeight - thumbHeight);

            for (int i = 0; i < thumbHeight; i++)
                screen.WriteChar(bounds.X, bounds.Y + 1 + thumbOffset + i, '█', style);
        }
    }
}
