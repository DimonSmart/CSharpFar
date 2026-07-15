using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationFunctionKeyBarRenderer
{
    private readonly ScreenRenderer _screen;
    private readonly IReadOnlyList<FunctionKeyBinding> _bindings;
    private readonly Func<string, bool> _canExecuteCommand;

    public ApplicationFunctionKeyBarRenderer(
        ScreenRenderer screen,
        IReadOnlyList<FunctionKeyBinding> bindings,
        Func<string, bool> canExecuteCommand)
    {
        _screen = screen;
        _bindings = bindings;
        _canExecuteCommand = canExecuteCommand;
    }

    public ApplicationFunctionKeyBarFrame? Render(ConsoleSize size, FunctionKeyLayer layer)
    {
        var actions = _bindings
            .Where(binding =>
                binding.Layer == layer &&
                _canExecuteCommand(binding.CommandId))
            .Select(binding => new FunctionKeyBarAction<string>(
                binding.KeyNumber,
                binding.Label,
                binding.CommandId))
            .ToArray();

        new FunctionKeyBarController<string>().Render(_screen, size.Height - 1, size.Width, actions);
        if (size.Height <= 0 || size.Width <= 0 || actions.Length == 0)
            return null;

        var hits = new List<ApplicationFunctionKeyHit>();
        var slotsByKey = FunctionKeyBar.BuildSlots(size.Height - 1, size.Width)
            .ToDictionary(slot => slot.KeyNumber, slot => slot.Bounds);
        foreach (var action in actions)
        {
            if (slotsByKey.TryGetValue(action.KeyNumber, out var bounds))
                hits.Add(new ApplicationFunctionKeyHit(bounds, action.Action));
        }

        return hits.Count > 0 ? new ApplicationFunctionKeyBarFrame(hits) : null;
    }
}
