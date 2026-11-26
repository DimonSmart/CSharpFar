namespace CSharpFar.App.Commands;

internal sealed class ApplicationCommandRegistry
{
    private readonly IReadOnlyDictionary<string, IApplicationCommand> _commands;

    public ApplicationCommandRegistry(IEnumerable<IApplicationCommand> commands)
    {
        _commands = commands.ToDictionary(command => command.CommandId, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> CommandIds => _commands.Keys.ToArray();

    public bool TryGetCommand(string commandId, out IApplicationCommand command) =>
        _commands.TryGetValue(commandId, out command!);

    public bool CanExecute(
        string commandId,
        object? args = null)
    {
        if (!TryGetCommand(commandId, out var command))
            throw new ArgumentOutOfRangeException(
                nameof(commandId),
                commandId,
                "Unsupported application command.");

        return command.CanExecute(args);
    }

    public ApplicationCommandResult Execute(
        string commandId,
        object? args = null)
    {
        if (!TryGetCommand(commandId, out var command))
            return ApplicationCommandResult.Failure($"Unsupported command: {commandId}");

        return command.Execute(args);
    }
}
