using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;

namespace CSharpFar.App.Commands;

internal sealed class CalculateDirectorySizesCommand : IApplicationCommand
{
    private readonly PanelDirectorySizeCoordinator? _directorySizes;

    public CalculateDirectorySizesCommand(PanelDirectorySizeCoordinator? directorySizes = null) =>
        _directorySizes = directorySizes;

    public string CommandId => FunctionKeyCommandIds.CalculateDirectorySizes;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        return _directorySizes?.CanCalculateAll(target.State) == true;
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        _directorySizes?.CalculateAll(target.Side, target.State);
        return ApplicationCommandResult.Rendered();
    }
}
