using CSharpFar.Console;
using CSharpFar.Console.Input;

using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

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
        Composition = UiTestHost.Create(Screen, new UiLayerTestSurface(Screen, layer)).Composition;
    }

    public FakeConsoleDriver Driver { get; }

    public ScreenRenderer Screen { get; }

    public UiCompositionHost Composition { get; }

    public void Render() => Composition.Render();

    public UiInputResult Dispatch(ConsoleInputEvent input) => Composition.DispatchInput(input);

    public void Resize(int width, int height) => Driver.SetSize(width, height);
}
