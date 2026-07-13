using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

internal sealed class RenderOnlyOverlayLayer : IUiLayer
{
    private readonly Action<UiRenderContext> _render;

    public RenderOnlyOverlayLayer(Action<UiRenderContext> render) =>
        _render = render;

    public UiLayerInputPolicy InputPolicy => UiLayerInputPolicy.None;

    public UiFocusScope FocusScope { get; } = new();

    public UiInteractionFrame CommittedInteractionFrame => UiInteractionFrame.Empty;

    public void Render(UiRenderContext context) =>
        _render(context);

    public UiInputResult RouteInput(
        ConsoleInputEvent input,
        UiInputRouteContext context) =>
        UiInputResult.NotHandled;
}
