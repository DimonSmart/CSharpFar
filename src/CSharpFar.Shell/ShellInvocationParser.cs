using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellInvocationParser
{
    private readonly ShellRegistry _registry;

    public ShellInvocationParser(ShellRegistry registry) => _registry = registry;

    public bool TryParse(string command, out ShellInvocation invocation)
    {
        int separator = command.IndexOf(':');
        if (separator <= 0 || !_registry.TryGet(command[..separator], out ShellProfile profile))
        {
            invocation = default!;
            return false;
        }

        int commandStart = separator + 1;
        while (commandStart < command.Length && command[commandStart] is ' ' or '\t') commandStart++;
        invocation = new ShellInvocation(profile.Id, command[commandStart..]);
        return true;
    }
}
