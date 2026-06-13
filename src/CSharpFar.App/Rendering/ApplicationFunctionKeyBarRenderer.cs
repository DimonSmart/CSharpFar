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
    }
}
