namespace CSharpFar.App.Commands;

internal sealed class SwapPanelsCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.SwapPanels;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasVisiblePanels;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.SwapPanels();
        return ApplicationCommandResult.Rendered();
    }
}
