using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class ApplicationFunctionKeyBarRenderer
{
    private readonly IReadOnlyList<FunctionKeyBinding> _bindings;
    private readonly Func<string, bool> _canExecuteCommand;

    public ApplicationFunctionKeyBarRenderer(
        IReadOnlyList<FunctionKeyBinding> bindings,
        Func<string, bool> canExecuteCommand)
    {
        _bindings = bindings;
        _canExecuteCommand = canExecuteCommand;
    }

    public ApplicationFunctionKeyBarFrame? Render(IUiCanvas canvas, ConsoleSize size, FunctionKeyLayer layer)
    {
        var visibleBindings = _bindings
            .Where(binding =>
                binding.Layer == layer &&
                _canExecuteCommand(binding.CommandId))
            .ToArray();
        var pointerActions = visibleBindings
            .Select(binding => new FunctionKeyBarAction<FunctionKeyBinding>(
                binding.KeyNumber,
                binding.Label,
                binding))
            .ToArray();
        var controller = new FunctionKeyBarController<FunctionKeyBinding>();
        controller.Render(canvas, size.Height - 1, size.Width, pointerActions);

        ApplicationFunctionKeyHit[] hits = size.Height <= 0 || size.Width <= 0
            ? []
            : controller.BuildActionHits(size.Height - 1, size.Width, pointerActions)
                .Select(hit => new ApplicationFunctionKeyHit(
                    hit.Bounds,
                    hit.Action.CommandId,
                    hit.Action.Layer,
                    hit.Action.Key,
                    hit.Action.RunsWhenUnavailable))
                .ToArray();
        ApplicationFunctionKeyAction[] keyboardActions = _bindings
            .Where(binding => _canExecuteCommand(binding.CommandId) || binding.RunsWhenUnavailable)
            .Select(binding => new ApplicationFunctionKeyAction(
                binding.CommandId,
                binding.Layer,
                binding.Key,
                binding.RunsWhenUnavailable))
            .ToArray();

        return keyboardActions.Length > 0
            ? new ApplicationFunctionKeyBarFrame(hits, keyboardActions)
            : null;
    }
}
