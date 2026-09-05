using CSharpFar.Console;

namespace CSharpFar.Tests;

internal static class UiTestRender
{
    public static TResult Render<TResult>(
        ScreenRenderer screen,
        Func<IUiCanvas, TResult> draw)
    {
        TResult result = default!;
        void Draw(IUiCanvas canvas) => result = draw(canvas);
        Render(screen, Draw);
        return result;
    }

    public static void Render(
        ScreenRenderer screen,
        Action<IUiCanvas> draw)
    {
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, context => draw(context.Canvas)));
        composition.Render();
    }
}
