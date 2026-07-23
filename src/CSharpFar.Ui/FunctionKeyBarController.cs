using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public readonly record struct FunctionKeyBarAction<TAction>(
    int KeyNumber,
    string Label,
    TAction Action,
    bool Enabled = true);

public readonly record struct FunctionKeyBarActionHit<TAction>(
    Rect Bounds,
    int KeyNumber,
    string Label,
    TAction Action)
{
    public string Key => $"F{KeyNumber}";
}

public sealed class FunctionKeyBarController<TAction>
{
    private readonly FunctionKeyBar _bar = new();
    private readonly UiTargetId? _interactionTarget;

    public FunctionKeyBarController()
    {
    }

    public FunctionKeyBarController(UiTargetId interactionTarget)
    {
        _interactionTarget = interactionTarget ?? throw new ArgumentNullException(nameof(interactionTarget));
    }

    public UiTargetId InteractionTarget =>
        _interactionTarget ?? throw new InvalidOperationException(
            "This function-key bar controller was created without an interaction target.");

    public void Render(
        IUiCanvas screen,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarAction<TAction>> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var items = actions
            .Where(action => action.Enabled)
            .Select(action => new FunctionKeyBarItem(action.KeyNumber, action.Label))
            .ToArray();

        _bar.Render(screen, y, totalWidth, items);
    }

    public IReadOnlyList<FunctionKeyBarActionHit<TAction>> BuildActionHits(
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarAction<TAction>> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        Dictionary<int, FunctionKeyBarAction<TAction>> actionsByKey = actions
            .Where(action => action.Enabled && action.KeyNumber is >= 1 and <= 12)
            .ToDictionary(action => action.KeyNumber);

        return FunctionKeyBar.BuildSlots(y, totalWidth)
            .Where(slot => actionsByKey.ContainsKey(slot.KeyNumber))
            .Select(slot =>
            {
                FunctionKeyBarAction<TAction> action = actionsByKey[slot.KeyNumber];
                return new FunctionKeyBarActionHit<TAction>(
                    slot.Bounds,
                    action.KeyNumber,
                    action.Label,
                    action.Action);
            })
            .ToArray();
    }

    public UiInteractionFragment BuildInteractionFragment(
        IReadOnlyList<FunctionKeyBarActionHit<TAction>> actionHits)
    {
        ArgumentNullException.ThrowIfNull(actionHits);
        UiTargetId target = InteractionTarget;
        UiHitRegion[] hitRegions = actionHits
            .Select(hit => new UiHitRegion(target, hit.Bounds))
            .ToArray();
        return new UiInteractionFragment(hitRegions, []);
    }

    public bool TryGetAction(
        MouseConsoleInputEvent mouse,
        UiInputRouteContext route,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarAction<TAction>> actions,
        out TAction action)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Target != InteractionTarget)
        {
            action = default!;
            return false;
        }

        return TryGetAction(mouse, y, totalWidth, actions, out action);
    }

    public bool TryGetAction(
        MouseConsoleInputEvent mouse,
        int y,
        int totalWidth,
        IReadOnlyList<FunctionKeyBarAction<TAction>> actions,
        out TAction action)
    {
        ArgumentNullException.ThrowIfNull(actions);
        action = default!;

        if (!_bar.TryHitTest(mouse, y, totalWidth, out var hit))
            return false;

        foreach (var candidate in actions)
        {
            if (candidate.Enabled && candidate.KeyNumber == hit.KeyNumber)
            {
                action = candidate.Action;
                return true;
            }
        }

        return false;
    }
}
