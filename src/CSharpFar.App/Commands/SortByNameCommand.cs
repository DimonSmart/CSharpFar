using CSharpFar.App.Panels;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByNameCommand : SortActivePanelCommand
{
    public SortByNameCommand(PanelCommandTargetResolver targetResolver, PanelSortServiceFacade panelSort)
        : base(FunctionKeyCommandIds.SortByName, SortMode.Name, targetResolver, panelSort)
    {
    }
}
