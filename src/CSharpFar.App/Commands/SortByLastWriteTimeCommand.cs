using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByLastWriteTimeCommand : SortActivePanelCommand
{
    public SortByLastWriteTimeCommand()
        : base(FunctionKeyCommandIds.SortByLastWriteTime, SortMode.LastWriteTime)
    {
    }
}
