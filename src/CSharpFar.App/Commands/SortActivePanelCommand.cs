using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal abstract class SortActivePanelCommand : IApplicationCommand
{
    protected SortActivePanelCommand(string commandId, SortMode sortMode)
    {
        CommandId = commandId;
        SortMode = sortMode;
    }

    public string CommandId { get; }

    protected SortMode SortMode { get; }

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.Enumerate);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        context.SetPanelSortMode(target.State, SortMode, target.VisibleRows);
        return ApplicationCommandResult.Rendered();
    }
}
