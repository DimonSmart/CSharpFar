using CSharpFar.Console;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ModalTestHost
{
    public static ModalDialogHost Create(FakeConsoleDriver driver)
    {
        var screen = new ScreenRenderer(driver);
        return Create(screen);
    }

    public static ModalDialogHost Create(ScreenRenderer screen)
    {
        var composition = new UiCompositionHost(screen);
        composition.SetRootSurface(new ScreenRendererSurface(screen, _ => { }));
        return new ModalDialogHost(composition);
    }
}
