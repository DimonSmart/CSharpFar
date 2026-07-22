using CSharpFar.App.Viewer;
using CSharpFar.Console;

namespace CSharpFar.Tests;

internal static class UiTestCanvas
{
    public static IUiCanvas Canvas(ScreenRenderer screen) => new ScreenRendererCanvas(screen);

    public static FileViewer FileViewerFor(ScreenRenderer screen)
    {
        UiTestHost host = UiTestHost.Create(screen);
        return new FileViewer(host.Surfaces, host.ModalDialogs);
    }
}
