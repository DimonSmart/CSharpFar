using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

internal static class ApplicationPanelInputHandlerTestExtensions
{
    public static ApplicationInputHandlingResult Handle(
        this ApplicationPanelInputHandler handler,
        MouseConsoleInputEvent input,
        ApplicationPanelFrame? frame,
        UiInputRouteKind routeKind)
    {
        if (frame is null)
            return ApplicationInputHandlingResult.NotHandled;

        UiTargetId? target = ApplicationTargetIds.Panel(frame.Side);
        if (routeKind == UiInputRouteKind.HitTarget)
        {
            var builder = new UiInteractionFrameBuilder()
                .AddHitRegion(ApplicationTargetIds.Panel(frame.Side), frame.Bounds);
            if (frame.RetryBounds is { } retryBounds)
                builder.AddHitRegion(ApplicationTargetIds.PanelRetry(frame.Side), retryBounds);
            foreach (ApplicationPanelItemHit item in frame.VisibleItems)
            {
                UiTargetId itemTarget = ApplicationTargetIds.PanelItem(frame.Side, item.ItemIndex);
                builder.AddHitRegion(itemTarget, item.Bounds);
                if (frame.Side == PanelSide.Right && item.Bounds.X == frame.Bounds.X + 1)
                    builder.AddHitRegion(itemTarget, new Rect(frame.Bounds.X, item.Bounds.Y, 1, item.Bounds.Height));
            }

            if (builder.Build().TryHitTest(input.X, input.Y, out UiHitRegion hit))
                target = hit.Target;
        }

        return handler.Handle(input, frame, routeKind, target);
    }
}
