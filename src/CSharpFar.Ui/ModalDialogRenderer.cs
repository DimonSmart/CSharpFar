using CSharpFar.Console;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class ModalDialogRenderer
{
    private const int OuterPaddingX = 1;
    private const int OuterPaddingY = 1;

    private readonly DialogFrameRenderer _frameRenderer = new();

    public readonly record struct Layout(
        Rect OuterBounds,
        Rect FrameBounds,
        Rect ContentBounds);

    public Rect CenteredOuterBounds(ScreenRenderer screen, int outerWidth, int outerHeight, int minWidth = 20, int minHeight = 8)
    {
        var size = screen.GetSize();
        int width = Math.Min(outerWidth, Math.Max(minWidth, size.Width - 2));
        int height = Math.Min(outerHeight, Math.Max(minHeight, size.Height - 2));
        return new Rect(
            Math.Max(0, (size.Width - width) / 2),
            Math.Max(0, (size.Height - height) / 2),
            width,
            height);
    }

    public void Render(
        ScreenRenderer screen,
        Rect outerBounds,
        string title,
        bool doubleBorder,
        PopupRenderOptions outerOptions,
        PopupRenderOptions frameOptions,
        Action<ScreenRenderer, Layout> renderContent)
    {
        new PopupRenderer().RenderPopup(
            screen,
            outerBounds,
            outerOptions,
            (_, _) =>
            {
                var frameBounds = new Rect(
                    outerBounds.X + OuterPaddingX,
                    outerBounds.Y + OuterPaddingY,
                    Math.Max(1, outerBounds.Width - OuterPaddingX * 2),
                    Math.Max(1, outerBounds.Height - OuterPaddingY * 2));

                _frameRenderer.RenderFrame(
                    screen,
                    frameBounds,
                    title,
                    doubleBorder,
                    frameOptions,
                    (_, contentBounds) => renderContent(screen, new Layout(outerBounds, frameBounds, contentBounds)));
            });
    }
}
