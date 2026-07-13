using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public abstract class UiLayer<TFrame> : IUiLayer
{
    private readonly UiCommittedState<TFrame> _committedFrame = new();
    private readonly UiCommittedState<UiInteractionFrame> _committedInteractionFrame = new(UiInteractionFrame.Empty);

    public abstract UiLayerInputPolicy InputPolicy { get; }

    public UiFocusScope FocusScope { get; } = new();

    public bool HasCommittedFrame => _committedFrame.HasValue;

    public TFrame CommittedFrame => _committedFrame.Value;

    public UiInteractionFrame CommittedInteractionFrame => _committedInteractionFrame.Value;

    public void Render(UiRenderContext context)
    {
        TFrame frame = RenderFrame(context);
        UiInteractionFrame interactionFrame = BuildInteractionFrame(frame) ??
            throw new InvalidOperationException("A UI layer cannot publish a null interaction frame.");
        _committedFrame.Stage(context, frame);
        _committedInteractionFrame.Stage(context, interactionFrame);
        context.PublishOnStable(interactionFrame.Focus, FocusScope.Commit);
        context.PublishOnStable(frame, OnFrameCommitted);
    }

    public UiInputResult RouteInput(
        ConsoleInputEvent input,
        UiInputRouteContext context) =>
        RouteInput(input, _committedFrame.Value, context);

    protected abstract TFrame RenderFrame(UiRenderContext context);

    protected abstract UiInputResult RouteInput(
        ConsoleInputEvent input,
        TFrame frame,
        UiInputRouteContext context);

    protected virtual UiInteractionFrame BuildInteractionFrame(TFrame frame) =>
        UiInteractionFrame.Empty;

    protected virtual void OnFrameCommitted(TFrame frame)
    {
    }
}
