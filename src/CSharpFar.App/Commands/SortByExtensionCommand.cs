using CSharpFar.App.Panels;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByExtensionCommand : SortActivePanelCommand
{
    public SortByExtensionCommand(PanelCommandTargetResolver targetResolver, PanelSortServiceFacade panelSort)
        : base(FunctionKeyCommandIds.SortByExtension, SortMode.Extension, targetResolver, panelSort)
    {
    }
}
