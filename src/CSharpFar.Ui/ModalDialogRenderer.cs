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
        return UiLayout.Center(size, Math.Max(minWidth, outerWidth), Math.Max(minHeight, outerHeight));
    }

    public Layout CalculateLayout(ConsoleSize size, int outerWidth, int outerHeight, int minWidth = 20, int minHeight = 8) =>
        CalculateLayout(CenteredOuterBounds(size, outerWidth, outerHeight, minWidth, minHeight));

    internal Layout CalculateLayout(Rect outerBounds)
    {
        if (outerBounds.Width < 3 || outerBounds.Height < 3)
            return new Layout(outerBounds, outerBounds, new Rect(outerBounds.X, outerBounds.Y, 0, 0));

        Rect frameBounds = UiLayout.Inset(outerBounds, OuterPaddingX, OuterPaddingY);
        return new Layout(outerBounds, frameBounds, UiLayout.Inset(frameBounds, 1, 1));
    }

    public void Render(
        IUiCanvas screen,
        Rect outerBounds,
        string title,
        bool doubleBorder,
        PopupRenderOptions outerOptions,
        PopupRenderOptions frameOptions,
        Action<IUiCanvas, Layout> renderContent)
        => Render(screen, CalculateLayout(outerBounds), title, doubleBorder, outerOptions, frameOptions, renderContent);

    public void Render(
        IUiCanvas screen,
        Layout layout,
        string title,
        bool doubleBorder,
        PopupRenderOptions outerOptions,
        PopupRenderOptions frameOptions,
        Action<IUiCanvas, Layout> renderContent)
    {
        Rect outerBounds = layout.OuterBounds;
        if (outerBounds.Width < 3 || outerBounds.Height < 3)
        {
            screen.FillRegion(outerBounds, outerOptions.BackgroundStyle);
            renderContent(screen, layout);
            return;
        }

        new PopupRenderer().RenderPopup(
            screen,
            outerBounds,
            outerOptions,
            (_, _) =>
            {
                _frameRenderer.RenderFrame(
                    screen,
                    layout.FrameBounds,
                    title,
                    doubleBorder,
                    frameOptions,
                    (_, _) => renderContent(screen, layout));
            });
    }
}
