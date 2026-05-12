using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SelectLeftDriveCommand : DriveSelectionCommand
{
    public SelectLeftDriveCommand()
        : base(FunctionKeyCommandIds.LeftVolume, PanelSide.Left)
    {
    }
}
