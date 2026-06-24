namespace CSharpFar.App.Commands;

internal sealed class TogglePanelsCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.TogglePanels;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null) =>
        context.TogglePanels()
            ? ApplicationCommandResult.Rendered()
            : ApplicationCommandResult.NotRendered();
}
