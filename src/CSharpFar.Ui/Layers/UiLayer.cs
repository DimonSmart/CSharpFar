using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public abstract class UiLayer<TFrame> : IUiLayer, IUiFocusRuntime
{
    private readonly UiCommittedState<TFrame> _committedFrame = new();
    private readonly UiCommittedState<UiInteractionFrame> _committedInteractionFrame = new(UiInteractionFrame.Empty);
    private readonly UiFocusController _focusController = new();

    public abstract UiLayerInputPolicy InputPolicy { get; }

    public IUiFocusState FocusState => _focusController;

    public bool HasCommittedFrame => _committedFrame.HasValue;

    public TFrame CommittedFrame => _committedFrame.Value;

    public UiInteractionFrame CommittedInteractionFrame => _committedInteractionFrame.Value;

    void IUiFocusRuntime.RequestFocusOnNextCommit(UiFocusRequest request) =>
        _focusController.RequestOnNextCommit(request);

    internal void RequestFocusOnNextCommit(UiFocusRequest request) =>
        _focusController.RequestOnNextCommit(request);

    public void Render(UiRenderContext context)
    {
        TFrame frame = RenderFrame(context);
        UiInteractionFrame interactionFrame = BuildInteractionFrame(frame) ??
            throw new InvalidOperationException("A UI layer cannot publish a null interaction frame.");
        ApplyCursor(context.RuntimeScreen, context.Viewport, interactionFrame);
        _committedFrame.Stage(context, frame);
        _committedInteractionFrame.Stage(context, interactionFrame);
        context.PublishOnStable(interactionFrame.Focus, _focusController.Commit);
        context.PublishOnStable(frame, OnFrameCommitted);
    }

    public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext hostContext)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(hostContext);

        TFrame frame = _committedFrame.Value;
        UiInteractionFrame interactionFrame = _committedInteractionFrame.Value;
        UiInputRouteContext route = ResolveRoute(input, interactionFrame, hostContext);
        return RouteInput(input, frame, route);
    }

    private UiInputRouteContext ResolveRoute(
        ConsoleInputEvent input,
        UiInteractionFrame interactionFrame,
        UiInputRouteContext hostContext)
    {
        if (!ReferenceEquals(hostContext.FocusState, FocusState))
            throw new InvalidOperationException("Input route context belongs to another UI layer.");

        if (hostContext.RouteKind == UiInputRouteKind.CapturedTarget)
        {
            if (input is not MouseConsoleInputEvent || hostContext.Target is null)
                throw new InvalidOperationException("Captured target routes require mouse input and a target.");
            return UiInputRouteContext.CapturedTarget(FocusState, hostContext.Target);
        }

        if (hostContext.RouteKind != UiInputRouteKind.Layer || hostContext.Target is not null)
            throw new InvalidOperationException("Only a layer route can be supplied for ordinary input dispatch.");

        return input switch
        {
            MouseConsoleInputEvent mouse when interactionFrame.TryHitTest(mouse.X, mouse.Y, out UiHitRegion region) =>
                UiInputRouteContext.HitTarget(FocusState, region.Target),
            MouseConsoleInputEvent => UiInputRouteContext.Layer(FocusState),
            KeyConsoleInputEvent or ModifierKeyConsoleInputEvent when interactionFrame.KeyboardTarget is UiTargetId target =>
                UiInputRouteContext.KeyboardTarget(FocusState, target),
            KeyConsoleInputEvent or ModifierKeyConsoleInputEvent when FocusState.FocusedTarget is UiTargetId target =>
                UiInputRouteContext.FocusedTarget(FocusState, target),
            _ => UiInputRouteContext.Layer(FocusState),
        };
    }

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

    private void ApplyCursor(
        ScreenRenderer screen,
        ConsoleViewport viewport,
        UiInteractionFrame interactionFrame)
    {
        if (interactionFrame.Focus.Entries.Count == 0)
            return;

        UiTargetId? focusedTarget = _focusController.ResolveFocusedTarget(interactionFrame.Focus);
        UiFocusEntry? focusedEntry = focusedTarget is null
            ? null
            : interactionFrame.Focus.Entries.FirstOrDefault(entry => entry.Target == focusedTarget);
        UiCursorPlacement? cursor = focusedEntry?.Cursor;
        if (cursor is not { Visible: true } placement ||
            !viewport.ContainsRelative(placement.X, placement.Y))
        {
            screen.SetCursorVisible(false);
            return;
        }

        screen.SetCursorPosition(placement.X, placement.Y);
        screen.SetCursorVisible(true);
    }
}
