using CSharpFar.Console;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal sealed class UiTestHost
{
    private UiTestHost(
        ScreenRenderer screen,
        Action<UiRenderContext>? rootRender)
        : this(screen, new ScreenRendererSurface(screen, rootRender ?? (_ => { })))
    {
    }

    private UiTestHost(
        ScreenRenderer screen,
        IUiSurface rootSurface)
    {
        Screen = screen;
        Composition = new UiCompositionHost(screen);
        Composition.SetRootSurface(rootSurface);
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

    public static UiTestHost Create(
        ScreenRenderer screen,
        IUiSurface rootSurface) =>
        new(screen, rootSurface);
}
