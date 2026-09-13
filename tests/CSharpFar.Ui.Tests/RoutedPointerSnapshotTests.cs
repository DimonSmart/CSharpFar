using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui.Tests;

public sealed class RoutedPointerSnapshotTests
{
    [Fact]
    public void Snapshot_RoutesCapturedRegionsAndPreservesOrdering()
    {
        var collection = new RoutedPointerCollection<int>(
            new UiTargetId("list"),
            item => new UiTargetId($"item:{item}"));
        RoutedPointerItem<int>[] items =
        [
            new(4, new Rect(1, 2, 8, 1)),
            new(4, new Rect(0, 2, 1, 1)),
            new(9, new Rect(1, 3, 8, 1)),
            new(10, new Rect(0, 0, 0, 0)),
        ];
        var retryTarget = new UiTargetId("retry");

        RoutedPointerSnapshot<int> snapshot = collection.Capture(
            new Rect(0, 0, 12, 8),
            items,
            [new UiHitRegion(retryTarget, new Rect(2, 5, 4, 1))]);

        Assert.Equal(
            ["list", "item:4", "item:4", "item:9", "retry"],
            snapshot.InteractionFragment.HitRegions.Select(region => region.Target.Value));
        Assert.True(snapshot.ContainsAdditionalTarget(retryTarget));

        AssertRoute(snapshot, "list", Mouse(MouseButton.Left, MouseEventKind.Down), RoutedPointerActionKind.SurfacePressed);
        AssertRoute(snapshot, "item:4", Mouse(MouseButton.Left, MouseEventKind.Down), RoutedPointerActionKind.ItemPrimaryPressed, 4);
        AssertRoute(snapshot, "item:4", Mouse(MouseButton.Left, MouseEventKind.DoubleClick), RoutedPointerActionKind.ItemDoubleClicked, 4);
        AssertRoute(snapshot, "item:4", Mouse(MouseButton.Right, MouseEventKind.Down), RoutedPointerActionKind.ItemSecondaryPressed, 4);
        AssertRoute(snapshot, "item:9", Mouse(MouseButton.WheelUp, MouseEventKind.Wheel), RoutedPointerActionKind.WheelUp, 9);
        AssertRoute(snapshot, "list", Mouse(MouseButton.WheelDown, MouseEventKind.Wheel), RoutedPointerActionKind.WheelDown);

        Assert.False(Route(snapshot, "missing", Mouse(MouseButton.Left, MouseEventKind.Down)).UiResult.Handled);
        Assert.False(Route(snapshot, "item:10", Mouse(MouseButton.Left, MouseEventKind.Down)).UiResult.Handled);

        RoutedPointerSnapshot<int> emptySurface = collection.Capture(new Rect(0, 0, 0, 0), []);
        Assert.Empty(emptySurface.InteractionFragment.HitRegions);
        Assert.False(Route(emptySurface, "list", Mouse(MouseButton.Left, MouseEventKind.Down)).UiResult.Handled);
    }

    [Fact]
    public void Snapshot_DoesNotObserveLaterSourceOrTargetMappingMutation()
    {
        string targetSuffix = "";
        var collection = new RoutedPointerCollection<int>(
            new UiTargetId("list"),
            item => new UiTargetId($"item:{item}{targetSuffix}"));
        var items = new List<RoutedPointerItem<int>>
        {
            new(4, new Rect(1, 2, 8, 1)),
        };
        var additional = new List<UiHitRegion>
        {
            new(new UiTargetId("retry"), new Rect(1, 4, 8, 1)),
        };

        RoutedPointerSnapshot<int> snapshot = collection.Capture(new Rect(0, 0, 12, 8), items, additional);
        string[] capturedRegions = snapshot.InteractionFragment.HitRegions
            .Select(region => $"{region.Target.Value}:{region.Bounds}")
            .ToArray();

        targetSuffix = "-new";
        items.Clear();
        items.Add(new RoutedPointerItem<int>(9, new Rect(20, 20, 1, 1)));
        additional.Clear();
        additional.Add(new UiHitRegion(new UiTargetId("other"), new Rect(20, 20, 1, 1)));

        Assert.Equal(
            capturedRegions,
            snapshot.InteractionFragment.HitRegions.Select(region => $"{region.Target.Value}:{region.Bounds}"));
        Assert.True(snapshot.ContainsAdditionalTarget(new UiTargetId("retry")));
        Assert.False(snapshot.ContainsAdditionalTarget(new UiTargetId("other")));

        RoutedPointerInput<int> routed = Route(snapshot, "item:4", Mouse(MouseButton.Left, MouseEventKind.Down));
        Assert.True(routed.UiResult.Handled);
        Assert.Equal(RoutedPointerActionKind.ItemPrimaryPressed, routed.Action.Kind);
        Assert.Equal(4, routed.Action.Item);
        Assert.False(Route(snapshot, "item:9-new", Mouse(MouseButton.Left, MouseEventKind.Down)).UiResult.Handled);
    }

    private static void AssertRoute(
        RoutedPointerSnapshot<int> snapshot,
        string target,
        MouseConsoleInputEvent input,
        RoutedPointerActionKind expectedKind,
        int? expectedItem = null)
    {
        RoutedPointerInput<int> routed = Route(snapshot, target, input);
        Assert.True(routed.UiResult.Handled);
        Assert.Equal(expectedKind, routed.Action.Kind);
        if (expectedItem is { } item)
            Assert.Equal(item, routed.Action.Item);
    }

    private static RoutedPointerInput<int> Route(
        RoutedPointerSnapshot<int> snapshot,
        string target,
        MouseConsoleInputEvent input) =>
        snapshot.RouteInput(
            input,
            UiInputRouteContext.HitTarget(new UiFocusController(), new UiTargetId(target)));

    private static MouseConsoleInputEvent Mouse(MouseButton button, MouseEventKind kind) =>
        new(0, 0, button, kind, MouseKeyModifiers.None);
}
