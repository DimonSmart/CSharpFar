using CSharpFar.App.FunctionKeys;
namespace CSharpFar.App.Commands;

internal sealed class HelpCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Help;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.ShowHelp();
        return ApplicationCommandResult.Rendered();
    }
}
