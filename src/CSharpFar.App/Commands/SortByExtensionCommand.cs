using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SortByExtensionCommand : SortActivePanelCommand
{
    public SortByExtensionCommand()
        : base(FunctionKeyCommandIds.SortByExtension, SortMode.Extension)
    {
    }
}
