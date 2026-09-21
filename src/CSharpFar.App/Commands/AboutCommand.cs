namespace CSharpFar.App.Commands;

internal sealed class AboutCommand : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.About;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.ShowAbout();
        return ApplicationCommandResult.Rendered();
    }
}
