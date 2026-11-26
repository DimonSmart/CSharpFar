namespace CSharpFar.App.Commands;

internal interface IApplicationCommand
{
    string CommandId { get; }

    bool CanExecute(object? args = null);

    ApplicationCommandResult Execute(object? args = null);
}
