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

        int slotWidth = size.Width / 12;
        if (slotWidth <= 0)
            return null;

        var hits = new List<ApplicationFunctionKeyHit>();
        foreach (var action in actions)
        {
            if (action.KeyNumber is < 1 or > 12)
                continue;

            int x = (action.KeyNumber - 1) * slotWidth;
            if (x >= size.Width)
                continue;

            int slotEnd = action.KeyNumber < 12 ? x + slotWidth : size.Width;
            int width = Math.Max(0, slotEnd - x);
            if (width > 0)
                hits.Add(new ApplicationFunctionKeyHit(new Rect(x, size.Height - 1, width, 1), action.Action));
        }

        return hits.Count > 0 ? new ApplicationFunctionKeyBarFrame(hits) : null;
    }
}
