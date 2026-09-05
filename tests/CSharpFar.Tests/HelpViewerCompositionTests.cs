using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
namespace CSharpFar.Tests;

public sealed class HelpViewerCompositionTests
{
    [Fact]
    public void HelpViewer_ResizeThenClose_RestoresCurrentRootSurface()
    {
        var driver = new FakeConsoleDriver(80, 25);
        var screen = new ScreenRenderer(driver);
        var host = new UiCompositionHost(screen);
        host.SetRootSurface(new ScreenRendererSurface(screen, context => { for (int y = 0; y < context.Size.Height; y++) context.Canvas.Write(0, y, new string('V', context.Size.Width), new CellStyle(ConsoleColor.Gray, ConsoleColor.Black)); }));
        host.Render();
        driver.SetSize(100, 35);
        driver.EnqueueInput(new ConsoleResizeInputEvent());
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, false, false, false));

        new HelpViewer(new InteractiveSurfaceHost(host)).Show();

        Assert.Equal(driver.GetViewport(), host.LastStableViewport);
        Assert.Equal('V', driver.GetCell(99, 34).Character);
    }

}


