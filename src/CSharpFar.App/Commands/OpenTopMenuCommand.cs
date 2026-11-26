using CSharpFar.App.Menu;
using CSharpFar.Core.Models;
using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class OpenTopMenuCommand(TopMenu topMenu, PanelCommandTargetResolver targetResolver) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.TopMenu;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null) =>
        Open(targetResolver.ResolvePanelTarget(args).Side); 

    private ApplicationCommandResult Open(PanelSide side)
    {
        topMenu.Open(side == PanelSide.Left ? "Left" : "Right");
        return ApplicationCommandResult.Rendered();
    }
}
