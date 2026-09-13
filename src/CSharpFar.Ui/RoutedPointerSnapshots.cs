using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Immutable pointer routing state captured for one rendered surface.</summary>
public sealed class RoutedPointerSnapshot<TItem>
{
    private readonly UiTargetId? _surfaceTarget;
    private readonly CapturedItem[] _items;
    private readonly UiTargetId[] _additionalTargets;

    internal RoutedPointerSnapshot(
        UiTargetId? surfaceTarget,
        CapturedItem[] items,
        UiTargetId[] additionalTargets,
        UiInteractionFragment interactionFragment)
    {
        _surfaceTarget = surfaceTarget;
        _items = items;
        _additionalTargets = additionalTargets;
        InteractionFragment = interactionFragment;
    }

    public UiInteractionFragment InteractionFragment { get; }

    public bool ContainsAdditionalTarget(UiTargetId target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _additionalTargets.Contains(target);
    }

    public RoutedPointerInput<TItem> RouteInput(
        MouseConsoleInputEvent input,
        UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(route);
        if (route.RouteKind != UiInputRouteKind.HitTarget || route.Target is null)
            return RoutedPointerInput<TItem>.NotHandled;

        TItem? item = default;
        bool hasItem = false;
        if (_surfaceTarget is not null && route.Target == _surfaceTarget)
        {
            hasItem = false;
        }
        else
        {
            foreach (CapturedItem candidate in _items)
            {
                if (candidate.Target == route.Target)
                {
                    item = candidate.Item;
                    hasItem = true;
                    break;
                }
            }
        }

        if (!hasItem && (_surfaceTarget is null || route.Target != _surfaceTarget))
            return RoutedPointerInput<TItem>.NotHandled;

        RoutedPointerActionKind? kind = input switch
        {
            { Button: MouseButton.Left, Kind: MouseEventKind.Down } when hasItem => RoutedPointerActionKind.ItemPrimaryPressed,
            { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick } when hasItem => RoutedPointerActionKind.ItemDoubleClicked,
            { Button: MouseButton.Right, Kind: MouseEventKind.Down } when hasItem => RoutedPointerActionKind.ItemSecondaryPressed,
            { Button: MouseButton.Left, Kind: MouseEventKind.Down } => RoutedPointerActionKind.SurfacePressed,
            { Button: MouseButton.WheelUp, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelUp,
            { Button: MouseButton.WheelDown, Kind: MouseEventKind.Wheel } => RoutedPointerActionKind.WheelDown,
            _ => null,
        };
        return kind is { } actionKind
            ? new RoutedPointerInput<TItem>(new RoutedPointerAction<TItem>(actionKind, item), UiInputResult.HandledResult)
            : RoutedPointerInput<TItem>.NotHandled;
    }

    internal readonly record struct CapturedItem(UiTargetId Target, TItem Item);
}

/// <summary>Captures immutable routed-pointer state without retaining caller-owned collections.</summary>
public static class RoutedPointerCollectionSnapshotExtensions
{
    public static RoutedPointerSnapshot<TItem> Capture<TItem>(
        this RoutedPointerCollection<TItem> collection,
        Rect bounds,
        IReadOnlyList<RoutedPointerItem<TItem>> items,
        IEnumerable<UiHitRegion>? additionalRegions = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);

        UiInteractionFragment fragment = collection.BuildInteractionFragment(bounds, items, additionalRegions);
        int regionIndex = 0;
        UiTargetId? surfaceTarget = null;
        if (IsInteractive(bounds))
            surfaceTarget = fragment.HitRegions[regionIndex++].Target;

        var capturedItems = new List<RoutedPointerSnapshot<TItem>.CapturedItem>();
        foreach (RoutedPointerItem<TItem> item in items)
        {
            if (!IsInteractive(item.Bounds))
                continue;

            UiTargetId target = fragment.HitRegions[regionIndex++].Target;
            capturedItems.Add(new RoutedPointerSnapshot<TItem>.CapturedItem(target, item.Item));
        }

        UiTargetId[] additionalTargets = fragment.HitRegions
            .Skip(regionIndex)
            .Select(region => region.Target)
            .ToArray();
        return new RoutedPointerSnapshot<TItem>(
            surfaceTarget,
            capturedItems.ToArray(),
            additionalTargets,
            fragment);
    }

    private static bool IsInteractive(Rect bounds) => bounds.Width > 0 && bounds.Height > 0;
}
