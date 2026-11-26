using CSharpFar.App.Panels;
namespace CSharpFar.App.Commands;

internal sealed class TogglePanelsCommand(ApplicationWorkspaceModeController workspaceMode) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.TogglePanels;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null) =>
        workspaceMode.TogglePanels()
            ? ApplicationCommandResult.Rendered()
            : ApplicationCommandResult.NotRendered();
}
