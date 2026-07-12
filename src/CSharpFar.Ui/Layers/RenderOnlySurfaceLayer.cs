using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

internal sealed class RenderOnlySurfaceLayer : IUiLayer
{
    private readonly IUiSurface _surface;

    public RenderOnlySurfaceLayer(IUiSurface surface) =>
        _surface = surface;

    public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.None;

    public UiFocusScope FocusScope { get; } = new();

    public void Render(UiRenderContext context) =>
        _surface.Render(context);

    public UiInputResult RouteInput(
        ConsoleInputEvent input,
        UiInputRouteContext context) =>
        UiInputResult.NotHandled;
}
