using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.Commands;

internal sealed class QuitCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Quit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        context.Running = false;
        return ApplicationCommandResult.NotRendered();
    }
}
