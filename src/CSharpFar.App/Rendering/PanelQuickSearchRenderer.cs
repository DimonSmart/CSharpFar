using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class PanelQuickSearchRenderer
{
    private const int PreferredWidth = 34;
    private const int MinimumWidth = 10;
    private const int OverlayHeight = 3;

    private readonly IUiCanvas _screen;
    private readonly ConsolePalette _palette;
    private readonly DialogFrameRenderer _frameRenderer = new();

    public PanelQuickSearchRenderer(IUiCanvas screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public PanelQuickSearchRenderLayout? Render(Rect panelBounds, string searchText)
    {
        int maxWidth = Math.Max(0, panelBounds.Width - 4);
        if (maxWidth < MinimumWidth || panelBounds.Height < OverlayHeight + 1)
            return null;

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
            int visibleStart = VisibleStart(searchText, contentBounds.Width);
            string visibleText = VisibleText(searchText, visibleStart, contentBounds.Width);
            _screen.Write(
                contentBounds.X,
                contentBounds.Y,
                visibleText.PadRight(contentBounds.Width),
                PaletteStyles.InputField(_palette));

            cursorX = contentBounds.X + searchText.Length - visibleStart;
            cursorY = contentBounds.Y;
        });

        return new PanelQuickSearchRenderLayout(
            bounds,
            new Rect(bounds.X + 1, bounds.Y + 1, Math.Max(0, bounds.Width - 2), 1),
            new UiCursorPlacement(cursorX, cursorY));
    }

    private static int VisibleStart(string text, int width) =>
        Math.Max(0, text.Length - Math.Max(0, width - 1));

    private static string VisibleText(string text, int visibleStart, int width)
    {
        if (width <= 0)
            return string.Empty;

        string visible = text.Length > visibleStart ? text[visibleStart..] : string.Empty;
        return visible.Length > width ? visible[..width] : visible;
    }
}

internal sealed record PanelQuickSearchRenderLayout(
    Rect PopupBounds,
    Rect InputBounds,
    UiCursorPlacement Cursor)
{
    public static implicit operator bool(PanelQuickSearchRenderLayout? layout) => layout is not null;
}
