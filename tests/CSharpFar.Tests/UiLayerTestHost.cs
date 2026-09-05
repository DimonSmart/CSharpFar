using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal sealed class UiLayerTestHost
{
    public UiLayerTestHost(IUiLayer layer, int width = 80, int height = 25)
        : this(layer, new FakeConsoleDriver(width, height))
    {
    }

    public UiLayerTestHost(IUiLayer layer, FakeConsoleDriver driver)
    {
        Driver = driver;
        Screen = new ScreenRenderer(Driver);
        UiTestHost host = UiTestHost.Create(Screen);
        Composition = host.Composition;
        _ = Composition.RegisterPersistentOverlay(layer);
    }

    public FakeConsoleDriver Driver { get; }

    public ScreenRenderer Screen { get; }

    public UiCompositionHost Composition { get; }

    public void Render() => Composition.Render();

    public UiInputResult Dispatch(ConsoleInputEvent input) => Composition.DispatchInput(input);

    public void Resize(int width, int height) => Driver.SetSize(width, height);
}
