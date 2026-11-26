using CSharpFar.App.Panels;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortBySizeCommand : SortActivePanelCommand
{
    public SortBySizeCommand(PanelCommandTargetResolver targetResolver, PanelSortServiceFacade panelSort)
        : base(FunctionKeyCommandIds.SortBySize, SortMode.Size, targetResolver, panelSort)
    {
    }
}
