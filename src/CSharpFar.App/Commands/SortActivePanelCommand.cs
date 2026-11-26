using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal abstract class SortActivePanelCommand : IApplicationCommand
{
    private readonly PanelCommandTargetResolver _targetResolver;
    private readonly PanelSortServiceFacade _panelSort;

    protected SortActivePanelCommand(string commandId, SortMode sortMode, PanelCommandTargetResolver targetResolver, PanelSortServiceFacade panelSort)
    {
        CommandId = commandId;
        SortMode = sortMode;
        _targetResolver = targetResolver;
        _panelSort = panelSort;
    }

    public string CommandId { get; }

    protected SortMode SortMode { get; }

    public bool CanExecute(object? args = null) =>
        (_targetResolver.ResolvePanelTarget(args).State.ProviderCapabilities & PanelProviderCapabilities.Enumerate) == PanelProviderCapabilities.Enumerate;

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = _targetResolver.ResolvePanelTarget(args);
        _panelSort.SetPanelSortMode(target.State, SortMode, target.VisibleRows);
        return ApplicationCommandResult.Rendered();
    }
}
