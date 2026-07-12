using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public abstract class UiLayer<TFrame> : IUiLayer
{
    private readonly UiCommittedState<TFrame> _committedFrame = new();

    public abstract UiLayerInputPolicy InputPolicy { get; }

    public UiFocusScope FocusScope { get; } = new();

    public bool HasCommittedFrame => _committedFrame.HasValue;

    public TFrame CommittedFrame => _committedFrame.Value;

    public void Render(UiRenderContext context)
    {
        TFrame frame = RenderFrame(context);
        _committedFrame.Stage(context, frame);
        UiFocusFrame focusFrame = BuildFocusFrame(frame);
        context.PublishOnStable(focusFrame, FocusScope.Commit);
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

    protected virtual UiFocusFrame BuildFocusFrame(TFrame frame) =>
        UiFocusFrame.Empty;

    protected virtual void OnFrameCommitted(TFrame frame)
    {
    }
}
