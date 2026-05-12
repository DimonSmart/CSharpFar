using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SelectRightDriveCommand : DriveSelectionCommand
{
    public SelectRightDriveCommand()
        : base(FunctionKeyCommandIds.RightVolume, PanelSide.Right)
    {
    }
}
