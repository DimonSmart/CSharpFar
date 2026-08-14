namespace CSharpFar.App.CommandLine;

internal sealed class EnvironmentVariableCommandExecutor
{
    public bool TryExecute(string command)
    {
        if (!OperatingSystem.IsWindows() ||
            !EnvironmentVariableCommandParser.TryParseAssignment(command, out string name, out string value))
        {
            return false;
        }

        Environment.SetEnvironmentVariable(
            name,
            value.Length == 0 ? null : value,
            EnvironmentVariableTarget.Process);
        return true;
    }
}
