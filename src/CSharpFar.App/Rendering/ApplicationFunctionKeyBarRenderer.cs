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

    public void Render(ConsoleSize size, FunctionKeyLayer layer)
    {
        var items = _bindings
            .Where(binding =>
                binding.Layer == layer &&
                _canExecuteCommand(binding.CommandId))
            .Select(binding => new FunctionKeyBarItem(binding.KeyNumber, binding.Label))
            .ToArray();

        new FunctionKeyBar().Render(_screen, size.Height - 1, size.Width, items);
    }
}
