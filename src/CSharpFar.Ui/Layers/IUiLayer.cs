using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public interface IUiLayer
{
    UiLayerInputPolicy InputPolicy { get; }

    UiFocusScope FocusScope { get; }

    UiInteractionFrame CommittedInteractionFrame { get; }

    void Render(UiRenderContext context);

    UiInputResult RouteInput(
        ConsoleInputEvent input,
        UiInputRouteContext context);
}
