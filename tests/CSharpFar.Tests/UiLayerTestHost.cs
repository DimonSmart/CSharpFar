using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal sealed class UiLayerTestHost
{
    public UiLayerTestHost(IUiLayer layer, int width = 80, int height = 25)
    {
        Driver = new FakeConsoleDriver(width, height);
        Screen = new ScreenRenderer(Driver);
        Composition = new UiCompositionHost(Screen);
        Composition.SetRootSurface(new LayerSurface(Screen, layer));
    }

    public FakeConsoleDriver Driver { get; }

    public ScreenRenderer Screen { get; }

    public UiCompositionHost Composition { get; }

    public void Render() => Composition.Render();

    public UiInputResult Dispatch(ConsoleInputEvent input) => Composition.DispatchInput(input);

    public void Resize(int width, int height) => Driver.SetSize(width, height);

    private sealed class LayerSurface(ScreenRenderer screen, IUiLayer layer) : IUiSurface, IUiLayer, IUiFocusRuntime
    {
        public UiLayerInputPolicy InputPolicy => layer.InputPolicy;
        public IUiFocusState FocusState => layer.FocusState;
        public UiInteractionFrame CommittedInteractionFrame => layer.CommittedInteractionFrame;
        public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();
        public void Render(UiRenderContext context) => layer.Render(context);
        public void CompleteFrame(UiFrameCompletion completion) { }
        public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) => layer.RouteInput(input, context);

        public void RequestFocusOnNextCommit(UiFocusRequest request)
        {
            if (layer is not IUiFocusRuntime runtime)
                throw new InvalidOperationException("The test layer does not support focus requests.");

            runtime.RequestFocusOnNextCommit(request);
        }
    }
}
