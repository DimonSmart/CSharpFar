namespace CSharpFar.Ui;

public sealed class UiInputRouteContext
{
    internal UiInputRouteContext(
        UiFocusScope focusScope,
        UiTargetId? capturedTarget,
        bool isCapturedRoute)
    {
        FocusScope = focusScope;
        CapturedTarget = capturedTarget;
        IsCapturedRoute = isCapturedRoute;
    }

    public UiFocusScope FocusScope { get; }

    public UiTargetId? CapturedTarget { get; }

    public bool IsCapturedRoute { get; }
}
