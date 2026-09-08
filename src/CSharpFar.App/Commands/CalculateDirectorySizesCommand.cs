using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class CalculateDirectorySizesCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.CalculateDirectorySizes;

    public bool CanExecute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        return context.DirectorySizes?.CanCalculateAll(target.State) == true;
    }

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!ApplicationCommandContext.CommittedLocationMatches(target.State, target.ActiveCommitted))
            return ApplicationCommandResult.Rendered();

        context.DirectorySizes?.CalculateAll(target.Side, target.State);
        return ApplicationCommandResult.Rendered();
    }
}
