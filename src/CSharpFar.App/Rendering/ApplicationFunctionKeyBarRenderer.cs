using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationFunctionKeyBarRenderer
{
    private readonly IUiCanvas _screen;
    private readonly IReadOnlyList<FunctionKeyBinding> _bindings;
    private readonly Func<string, bool> _canExecuteCommand;

    public ApplicationFunctionKeyBarRenderer(
        IUiCanvas screen,
        IReadOnlyList<FunctionKeyBinding> bindings,
        Func<string, bool> canExecuteCommand)
    {
        _screen = screen;
        _bindings = bindings;
        _canExecuteCommand = canExecuteCommand;
    }

    public ApplicationFunctionKeyBarFrame? Render(ConsoleSize size, FunctionKeyLayer layer)
    {
        var visibleBindings = _bindings
            .Where(binding =>
                binding.Layer == layer &&
                _canExecuteCommand(binding.CommandId))
            .ToArray();
        var actions = visibleBindings
            .Select(binding => new FunctionKeyBarAction<string>(
                binding.KeyNumber,
                binding.Label,
                binding.CommandId))
            .ToArray();

        new FunctionKeyBarController<string>().Render(_screen, size.Height - 1, size.Width, actions);

        var hits = new List<ApplicationFunctionKeyHit>();
        Dictionary<int, Rect> slotsByKey = size.Height <= 0 || size.Width <= 0
            ? []
            : FunctionKeyBar.BuildSlots(size.Height - 1, size.Width)
                .ToDictionary(slot => slot.KeyNumber, slot => slot.Bounds);
        foreach (var binding in _bindings)
        {
            bool available = _canExecuteCommand(binding.CommandId) || binding.RunsWhenUnavailable;
            if (!available)
                continue;

            Rect bounds = binding.Layer == layer && slotsByKey.TryGetValue(binding.KeyNumber, out var slotBounds)
                ? slotBounds
                : new Rect(0, 0, 0, 0);
            hits.Add(new ApplicationFunctionKeyHit(
                bounds,
                binding.CommandId,
                binding.Layer,
                binding.Key,
                binding.RunsWhenUnavailable));
        }

        return hits.Count > 0 ? new ApplicationFunctionKeyBarFrame(hits) : null;
    }
}
