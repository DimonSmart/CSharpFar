using CSharpFar.Console;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal sealed class UiTestHost
{
    private UiTestHost(
        ScreenRenderer screen,
        Action<UiRenderContext>? rootRender)
    {
        Screen = screen;
        Composition = new UiCompositionHost(screen);
        Composition.SetRootSurface(new ScreenRendererSurface(
            screen,
            rootRender ?? (_ => { })));
        ModalDialogs = new ModalDialogHost(Composition);
    }

    public ScreenRenderer Screen { get; }

    public UiCompositionHost Composition { get; }

    public ModalDialogHost ModalDialogs { get; }

    public static UiTestHost Create(
        FakeConsoleDriver driver,
        Action<UiRenderContext>? rootRender = null) =>
        new(new ScreenRenderer(driver), rootRender);

    public static UiTestHost Create(
        ScreenRenderer screen,
        Action<UiRenderContext>? rootRender = null) =>
        new(screen, rootRender);
}
