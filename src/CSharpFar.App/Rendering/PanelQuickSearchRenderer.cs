using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.App.Rendering;

internal sealed class PanelQuickSearchRenderer
{
    private const int PreferredWidth = 34;
    private const int MinimumWidth = 10;
    private const int OverlayHeight = 3;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly DialogFrameRenderer _frameRenderer = new();

    public PanelQuickSearchRenderer(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public bool Render(Rect panelBounds, string searchText)
    {
        int maxWidth = Math.Max(0, panelBounds.Width - 4);
        if (maxWidth < MinimumWidth || panelBounds.Height < OverlayHeight + 1)
            return false;

        int width = Math.Min(PreferredWidth, maxWidth);
        int x = panelBounds.X + Math.Max(1, (panelBounds.Width - width) / 2);
        int y = Math.Max(panelBounds.Y + 1, panelBounds.Bottom - OverlayHeight - 1);
        var bounds = new Rect(x, y, width, OverlayHeight);
        var options = PaletteStyles.DialogPopupOptions(_palette) with
        {
            DrawShadow = false,
        };

        int cursorX = bounds.X + 1;
        int cursorY = bounds.Y + 1;
        _frameRenderer.RenderFrame(_screen, bounds, "Search", false, options, (_, contentBounds) =>
        {
            string visibleText = VisibleTail(searchText, contentBounds.Width);
            _screen.Write(
                contentBounds.X,
                contentBounds.Y,
                visibleText.PadRight(contentBounds.Width),
                PaletteStyles.InputField(_palette));

            cursorX = contentBounds.X + Math.Min(visibleText.Length, Math.Max(0, contentBounds.Width - 1));
            cursorY = contentBounds.Y;
        });

        _screen.SetCursorPosition(cursorX, cursorY);
        _screen.SetCursorVisible(true);
        return true;
    }

    private static string VisibleTail(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text : text[^width..];
    }
}
