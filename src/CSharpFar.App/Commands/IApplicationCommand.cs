namespace CSharpFar.App.Commands;

internal interface IApplicationCommand
{
    string CommandId { get; }

    bool CanExecute(ApplicationCommandContext context, object? args = null);

    ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null);
}
