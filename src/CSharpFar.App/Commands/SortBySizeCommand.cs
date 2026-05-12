using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortBySizeCommand : SortActivePanelCommand
{
    public SortBySizeCommand()
        : base(FunctionKeyCommandIds.SortBySize, SortMode.Size)
    {
    }
}
