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
        context.HasCapability(State(context, args), PanelProviderCapabilities.Enumerate);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        PanelSide side = args is PanelCommandArgs panelArgs
            ? panelArgs.PanelSide
            : context.ActiveSide;
        context.SetPanelSortMode(context.GetPanelState(side), SortMode, context.VisibleRows(side));
        return ApplicationCommandResult.Rendered();
    }

    private static FilePanelState State(ApplicationCommandContext context, object? args) =>
        args is PanelCommandArgs panelArgs
            ? context.GetPanelState(panelArgs.PanelSide)
            : context.ActiveState;
}
