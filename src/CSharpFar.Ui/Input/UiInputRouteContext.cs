namespace CSharpFar.Ui;

public sealed class UiInputRouteContext
{
    private UiInputRouteContext(
        UiFocusScope focusScope,
        UiTargetId? target,
        UiInputRouteKind routeKind)
    {
        ArgumentNullException.ThrowIfNull(focusScope);
        if ((routeKind == UiInputRouteKind.Layer) != (target is null))
            throw new ArgumentException("Layer routes require no target and target routes require a target.", nameof(target));

        FocusScope = focusScope;
        Target = target;
        RouteKind = routeKind;
    }

    public UiFocusScope FocusScope { get; }

    public UiTargetId? Target { get; }

    public UiInputRouteKind RouteKind { get; }

    public bool IsTargetRoute => Target is not null;

    public bool IsCapturedRoute => RouteKind == UiInputRouteKind.CapturedTarget;

    internal static UiInputRouteContext Layer(UiFocusScope focusScope) =>
        new(focusScope, null, UiInputRouteKind.Layer);

    internal static UiInputRouteContext FocusedTarget(UiFocusScope focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.FocusedTarget);

    internal static UiInputRouteContext HitTarget(UiFocusScope focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.HitTarget);

    internal static UiInputRouteContext CapturedTarget(UiFocusScope focusScope, UiTargetId target) =>
        new(focusScope, target, UiInputRouteKind.CapturedTarget);
}
