using CSharpFar.Console;
using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public readonly record struct FunctionKeyBarAction<TAction>(
    int KeyNumber,
    string Label,
    TAction Action,
    bool Enabled = true);

public sealed class FunctionKeyBarController<TAction>
{
    private readonly FunctionKeyBar _bar = new();

    public void Render(
        ScreenRenderer screen,
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
