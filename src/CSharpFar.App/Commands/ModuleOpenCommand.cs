using CSharpFar.App.Modules;
using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;

namespace CSharpFar.App.Commands;

internal sealed record ModuleOpenCommandArgs(Guid ActionId);

internal sealed class ModuleOpenCommand(ModulePanelOpener modulePanelOpener, PanelWorkspaceController workspace) : IApplicationCommand
{
    public string CommandId => MenuCommandIds.ModuleOpen;

    public bool CanExecute(object? args = null) =>
        args is ModuleOpenCommandArgs;

    public ApplicationCommandResult Execute(object? args = null)
    {
        if (args is not ModuleOpenCommandArgs moduleArgs)
            return ApplicationCommandResult.Failure("Module command arguments are required.");

        return modulePanelOpener.OpenMenuItem(moduleArgs.ActionId, workspace.ActiveSide);
    }
}
