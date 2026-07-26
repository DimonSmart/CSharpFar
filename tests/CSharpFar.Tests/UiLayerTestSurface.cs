using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal sealed class UiLayerTestSurface(ScreenRenderer screen, IUiLayer layer) : IUiSurface, IUiLayer, IUiFocusRuntime
{
    public UiLayerInputPolicy InputPolicy => layer.InputPolicy;

    public IUiFocusState FocusState => layer.FocusState;

    public UiInteractionFrame CommittedInteractionFrame => layer.CommittedInteractionFrame;

    public IDisposable BeginFrame(UiRenderRequest request) => screen.BeginFrame();

    public void Render(UiRenderContext context) => layer.Render(context);

    public void CompleteFrame(UiFrameCompletion completion) { }

    public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext context) =>
        layer.RouteInput(input, context);

    public void RequestFocusOnNextCommit(UiFocusRequest request)
    {
        if (layer is not IUiFocusRuntime runtime)
            throw new InvalidOperationException("The test layer does not support focus requests.");

        runtime.RequestFocusOnNextCommit(request);
    }
}
