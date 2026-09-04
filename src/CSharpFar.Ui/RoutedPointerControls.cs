using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Describes one semantic item that can be addressed by a pointer.</summary>
public readonly record struct RoutedPointerItem<TItem>(TItem Item, Rect Bounds);

public enum RoutedPointerActionKind
{
    SurfacePressed,
    ItemPrimaryPressed,
    ItemDoubleClicked,
    ItemSecondaryPressed,
    WheelUp,
    WheelDown,
}

/// <summary>A pointer gesture resolved against the items of one committed surface.</summary>
public readonly record struct RoutedPointerAction<TItem>(
    RoutedPointerActionKind Kind,
    TItem? Item = default)
{
    public bool HasItem => Item is not null;
}

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

    public RoutedPointerInput<TItem> RouteInput(
        MouseConsoleInputEvent input,
        UiInputRouteContext route,
        IReadOnlyList<RoutedPointerItem<TItem>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (route.RouteKind != UiInputRouteKind.HitTarget || route.Target is null)
            return RoutedPointerInput<TItem>.NotHandled;

        TItem? item = default;
        bool hasItem = false;
        if (route.Target == _surfaceTarget)
        {
            hasItem = false;
        }
        else
        {
            foreach (RoutedPointerItem<TItem> candidate in items)
            {
                if (_itemTarget(candidate.Item) == route.Target)
                {
                    item = candidate.Item;
                    hasItem = true;
                    break;
                }
            }
        }

        if (!hasItem && route.Target != _surfaceTarget)
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

    private static bool IsInteractive(Rect bounds) => bounds.Width > 0 && bounds.Height > 0;
}

public readonly record struct RoutedPointerInput<TItem>(RoutedPointerAction<TItem> Action, UiInputResult UiResult)
{
    public static RoutedPointerInput<TItem> NotHandled { get; } = new(default, UiInputResult.NotHandled);
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
                    null, UiInputResult.ReleaseMouse())
                : RoutedScrollbarSurfaceInput.NotHandled;
        }

        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(input, frame.Value);
        return result.IsHandled
            ? new RoutedScrollbarSurfaceInput(result.PositionChanged ? result.FirstVisibleIndex : null, VerticalScrollbarRouting.ToUiInputResult(result, ScrollbarTarget))
            : RoutedScrollbarSurfaceInput.NotHandled;
    }
}

public readonly record struct RoutedScrollbarSurfaceInput(int? FirstVisibleIndex, UiInputResult UiResult)
{
    public static RoutedScrollbarSurfaceInput NotHandled { get; } =
        new(null, UiInputResult.NotHandled);
}

public enum RoutedPointerSelectionActionKind
{
    CursorRequested,
    SelectionStarted,
    SelectionExtended,
    SelectionCompleted,
    WordSelectionRequested,
    SecondaryActionRequested,
}

public readonly record struct RoutedPointerSelectionAction<TPosition>(RoutedPointerSelectionActionKind Kind, TPosition? Position = default);

/// <summary>Maps press, drag and release protocol to text-like pointer selection actions.</summary>
public sealed class RoutedPointerSelectionSurface<TPosition>
{
    private readonly UiTargetId _target;
    private readonly Func<int, int, TPosition> _positionResolver;

    public RoutedPointerSelectionSurface(UiTargetId target, Func<int, int, TPosition> positionResolver)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _positionResolver = positionResolver ?? throw new ArgumentNullException(nameof(positionResolver));
    }

    public UiInteractionFragment BuildInteractionFragment(Rect bounds) =>
        bounds.Width > 0 && bounds.Height > 0 ? new UiInteractionFragment([new UiHitRegion(_target, bounds)], []) : UiInteractionFragment.Empty;

    public RoutedPointerSelectionInput<TPosition> RouteInput(MouseConsoleInputEvent input, UiInputRouteContext route)
    {
        if (route.Target != _target)
            return RoutedPointerSelectionInput<TPosition>.NotHandled;
        TPosition Position() => _positionResolver(input.X, input.Y);
        return (route.RouteKind, input) switch
        {
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Down }) => new(new(RoutedPointerSelectionActionKind.SelectionStarted, Position()), UiInputResult.CaptureMouse(_target, MouseButton.Left)),
            (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Move }) => new(new(RoutedPointerSelectionActionKind.SelectionExtended, Position()), UiInputResult.HandledResult),
            (UiInputRouteKind.CapturedTarget, { Button: MouseButton.Left, Kind: MouseEventKind.Up }) => new(new(RoutedPointerSelectionActionKind.SelectionCompleted), UiInputResult.ReleaseMouse()),
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Left, Kind: MouseEventKind.DoubleClick }) => new(new(RoutedPointerSelectionActionKind.WordSelectionRequested, Position()), UiInputResult.HandledResult),
            (UiInputRouteKind.HitTarget, { Button: MouseButton.Right, Kind: MouseEventKind.Down }) => new(new(RoutedPointerSelectionActionKind.SecondaryActionRequested, Position()), UiInputResult.HandledResult),
            _ => RoutedPointerSelectionInput<TPosition>.NotHandled,
        };
    }
}

public readonly record struct RoutedPointerSelectionInput<TPosition>(RoutedPointerSelectionAction<TPosition> Action, UiInputResult UiResult)
{
    public static RoutedPointerSelectionInput<TPosition> NotHandled { get; } = new(default, UiInputResult.NotHandled);
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
