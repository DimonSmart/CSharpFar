using CSharpFar.Console;

namespace CSharpFar.Tests;

internal static class UiTestRender
{
    public static TResult Render<TResult>(
        ScreenRenderer screen,
        Func<IUiCanvas, TResult> draw)
    {
        using IDisposable frame = screen.BeginFrame();
        return draw(new ScreenRendererCanvas(screen));
    }

    public static void Render(
        ScreenRenderer screen,
        Action<IUiCanvas> draw)
    {
        using IDisposable frame = screen.BeginFrame();
        draw(new ScreenRendererCanvas(screen));
    }
}
