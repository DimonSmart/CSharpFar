using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByNameCommand : SortActivePanelCommand
{
    public SortByNameCommand()
        : base(FunctionKeyCommandIds.SortByName, SortMode.Name)
    {
    }
}
