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

    public Rect CenteredOuterBounds(ConsoleSize size, int outerWidth, int outerHeight, int minWidth = 20, int minHeight = 8)
    {
        int width = Math.Clamp(Math.Max(minWidth, outerWidth), 0, Math.Max(0, size.Width));
        int height = Math.Clamp(Math.Max(minHeight, outerHeight), 0, Math.Max(0, size.Height));
        return new Rect(
            Math.Max(0, (size.Width - width) / 2),
            Math.Max(0, (size.Height - height) / 2),
            width,
            height);
    }

    public void Render(
        IUiCanvas screen,
        Rect outerBounds,
        string title,
        bool doubleBorder,
        PopupRenderOptions outerOptions,
        PopupRenderOptions frameOptions,
        Action<IUiCanvas, Layout> renderContent)
    {
        if (outerBounds.Width < 3 || outerBounds.Height < 3)
        {
            screen.FillRegion(outerBounds, outerOptions.BackgroundStyle);
            renderContent(screen, new Layout(outerBounds, outerBounds, new Rect(outerBounds.X, outerBounds.Y, 0, 0)));
            return;
        }

        new PopupRenderer().RenderPopup(
            screen,
            outerBounds,
            outerOptions,
            (_, _) =>
            {
                var frameBounds = new Rect(
                    outerBounds.X + OuterPaddingX,
                    outerBounds.Y + OuterPaddingY,
                    outerBounds.Width - OuterPaddingX * 2,
                    outerBounds.Height - OuterPaddingY * 2);

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
