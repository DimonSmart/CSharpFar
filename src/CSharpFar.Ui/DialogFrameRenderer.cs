using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class DialogFrameRenderer
{
    private readonly PopupRenderer _popupRenderer = new();

    public void RenderFrame(
        ScreenRenderer screen,
        Rect bounds,
        string title,
        bool doubleBorder,
        PopupRenderOptions options,
        ScrollState? verticalScrollState,
        Action<ScreenRenderer, Rect> renderContent)
    {
        var popupOptions = options with
        {
            DrawBorder = true,
            DrawDoubleBorder = doubleBorder,
            VerticalScrollState = verticalScrollState,
        };
        _popupRenderer.RenderPopup(screen, bounds, popupOptions, (renderer, contentBounds) =>
        {
            renderContent(renderer, contentBounds);

            if (title.Length == 0 || bounds.Width <= 0)
                return;

            string titleText = $" {title} ";
            if (titleText.Length > bounds.Width)
                titleText = titleText[..bounds.Width];

            int titleX = bounds.X + Math.Max(0, (bounds.Width - titleText.Length) / 2);
            renderer.Write(titleX, bounds.Y, titleText, options.TitleStyle ?? options.BorderStyle);
        });
    }

    public void RenderFrame(
        ScreenRenderer screen,
        Rect bounds,
        string title,
        bool doubleBorder,
        PopupRenderOptions options,
        Action<ScreenRenderer, Rect> renderContent) =>
        RenderFrame(screen, bounds, title, doubleBorder, options, null, renderContent);
}
