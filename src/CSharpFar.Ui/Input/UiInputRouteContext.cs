namespace CSharpFar.Ui;

public sealed class UiInputRouteContext
{
    private UiInputRouteContext(
        IUiFocusState focusScope,
        UiTargetId? target,
        UiInputRouteKind routeKind)
    {
        ArgumentNullException.ThrowIfNull(focusScope);
        if ((routeKind == UiInputRouteKind.Layer) != (target is null))
            throw new ArgumentException("Layer routes require no target and target routes require a target.", nameof(target));

        FocusState = focusScope;
        Target = target;
        RouteKind = routeKind;
    }

    public IUiFocusState FocusState { get; }

    public UiTargetId? Target { get; }

    public UiInputRouteKind RouteKind { get; }

    public bool IsTargetRoute => Target is not null;

    public bool IsCapturedRoute => RouteKind == UiInputRouteKind.CapturedTarget;

    internal static UiInputRouteContext Layer(IUiFocusState focusScope) =>
        new(focusScope, null, UiInputRouteKind.Layer);

    internal static UiInputRouteContext FocusedTarget(IUiFocusState focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.FocusedTarget);

    internal static UiInputRouteContext KeyboardTarget(IUiFocusState focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.KeyboardTarget);

    internal static UiInputRouteContext HitTarget(IUiFocusState focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.HitTarget);

    internal static UiInputRouteContext CapturedTarget(IUiFocusState focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.CapturedTarget);
}
