using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

/// <summary>Describes one semantic item that can be addressed by a pointer.</summary>
public readonly record struct RoutedPointerItem<TItem>(TItem Item, Rect Bounds);

/// <summary>Maps a rendered set of semantic items to routed UI targets.</summary>
public sealed class RoutedPointerCollection<TItem>
{
    private readonly UiTargetId _surfaceTarget;
    private readonly Func<TItem, UiTargetId> _itemTarget;

    public RoutedPointerCollection(UiTargetId surfaceTarget, Func<TItem, UiTargetId> itemTarget)
    {
        _surfaceTarget = surfaceTarget ?? throw new ArgumentNullException(nameof(surfaceTarget));
        _itemTarget = itemTarget ?? throw new ArgumentNullException(nameof(itemTarget));
    }

    public UiInteractionFragment BuildInteractionFragment(
        Rect bounds,
        IReadOnlyList<RoutedPointerItem<TItem>> items,
        IEnumerable<UiHitRegion>? additionalRegions = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var regions = new List<UiHitRegion>();
        if (IsInteractive(bounds))
            regions.Add(new UiHitRegion(_surfaceTarget, bounds));
        foreach (RoutedPointerItem<TItem> item in items)
        {
            if (IsInteractive(item.Bounds))
                regions.Add(new UiHitRegion(_itemTarget(item.Item), item.Bounds));
        }
        if (additionalRegions is not null)
            regions.AddRange(additionalRegions.Where(region => IsInteractive(region.Bounds)));
        return new UiInteractionFragment(regions, []);
    }

    public bool TryGetItem(UiTargetId? target, IReadOnlyList<RoutedPointerItem<TItem>> items, out TItem item)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (target is not null)
        {
            foreach (RoutedPointerItem<TItem> candidate in items)
            {
                if (_itemTarget(candidate.Item) == target)
                {
                    item = candidate.Item;
                    return true;
                }
            }
        }
        item = default!;
        return false;
    }

    private static bool IsInteractive(Rect bounds) => bounds.Width > 0 && bounds.Height > 0;
}

/// <summary>
/// Owns the generic scrollbar lifecycle for a routed scrollable surface. The caller supplies
/// semantic items and applies the returned scroll request to its own state.
/// </summary>
public sealed class RoutedScrollbarSurface
{
    private readonly VerticalScrollbarController _scrollbar = new();

    public RoutedScrollbarSurface(UiTargetId scrollbarTarget)
    {
        ScrollbarTarget = scrollbarTarget ?? throw new ArgumentNullException(nameof(scrollbarTarget));
    }

    public UiTargetId ScrollbarTarget { get; }

    public VerticalScrollbarFrame? CalculateFrame(Rect? bounds, ScrollState? state) =>
        _scrollbar.CalculateFrame(bounds, state);

    public void ApplyCommittedFrame(VerticalScrollbarFrame? frame) => _scrollbar.ApplyCommittedFrame(frame);

    public UiInteractionFragment BuildInteractionFragment(Rect? bounds, VerticalScrollbarFrame? frame)
    {
        if (bounds is not { } effectiveBounds || frame is null ||
            effectiveBounds.Width <= 0 || effectiveBounds.Height <= 0)
        {
            return UiInteractionFragment.Empty;
        }
        return new UiInteractionFragment([new UiHitRegion(ScrollbarTarget, effectiveBounds)], []);
    }

    public RoutedScrollbarSurfaceInput RouteInput(
        MouseConsoleInputEvent input,
        VerticalScrollbarFrame? frame,
        UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Target != ScrollbarTarget ||
            route.RouteKind is not (UiInputRouteKind.HitTarget or UiInputRouteKind.CapturedTarget))
        {
            return RoutedScrollbarSurfaceInput.NotHandled;
        }

        if (frame is null)
        {
            return route.RouteKind == UiInputRouteKind.CapturedTarget &&
                   input is { Button: MouseButton.Left, Kind: MouseEventKind.Up }
                ? new RoutedScrollbarSurfaceInput(
                    VerticalScrollbarInputResult.NotHandled(), UiInputResult.ReleaseMouse())
                : RoutedScrollbarSurfaceInput.NotHandled;
        }

        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(input, frame.Value);
        return result.IsHandled
            ? new RoutedScrollbarSurfaceInput(result, VerticalScrollbarRouting.ToUiInputResult(result, ScrollbarTarget))
            : RoutedScrollbarSurfaceInput.NotHandled;
    }
}

public readonly record struct RoutedScrollbarSurfaceInput(
    VerticalScrollbarInputResult Scrollbar,
    UiInputResult UiResult)
{
    public static RoutedScrollbarSurfaceInput NotHandled { get; } =
        new(VerticalScrollbarInputResult.NotHandled(), UiInputResult.NotHandled);
}

/// <summary>Provides target-scoped pointer capture for controls that select while dragging.</summary>
public sealed class RoutedPointerCaptureSurface
{
    public RoutedPointerCaptureSurface(UiTargetId target, MouseButton button = MouseButton.Left)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Button = button;
    }

    public UiTargetId Target { get; }
    public MouseButton Button { get; }

    public UiInteractionFragment BuildInteractionFragment(Rect bounds) =>
        bounds.Width > 0 && bounds.Height > 0
            ? new UiInteractionFragment([new UiHitRegion(Target, bounds)], [])
            : UiInteractionFragment.Empty;

    public UiInputResult RouteInput(ConsoleInputEvent input, UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(route);
        if (input is not MouseConsoleInputEvent mouse || route.Target != Target)
            return UiInputResult.NotHandled;
        if (route.RouteKind == UiInputRouteKind.HitTarget && mouse.Kind == MouseEventKind.Down && mouse.Button == Button)
            return UiInputResult.CaptureMouse(Target, Button);
        if (route.RouteKind == UiInputRouteKind.CapturedTarget && mouse.Kind == MouseEventKind.Up && mouse.Button == Button)
            return UiInputResult.ReleaseMouse();
        return UiInputResult.HandledResult;
    }
}
