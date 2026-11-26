using CSharpFar.App.Panels;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByLastWriteTimeCommand : SortActivePanelCommand
{
    public SortByLastWriteTimeCommand(PanelCommandTargetResolver targetResolver, PanelSortServiceFacade panelSort)
        : base(FunctionKeyCommandIds.SortByLastWriteTime, SortMode.LastWriteTime, targetResolver, panelSort)
    {
    }
}
