using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public interface IUiLayer
{
    UiLayerInputPolicy InputPolicy { get; }

    IUiFocusState FocusState { get; }

    UiInteractionFrame CommittedInteractionFrame { get; }

    void Render(UiRenderContext context);

    UiInputResult RouteInput(
        ConsoleInputEvent input,
        UiInputRouteContext context);
}

internal interface IUiFocusRuntime
{
    void RequestFocusOnNextCommit(UiFocusRequest request);
}
