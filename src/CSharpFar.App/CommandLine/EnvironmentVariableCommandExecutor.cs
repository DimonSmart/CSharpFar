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

        string? expandedValue = value.Length == 0
            ? null
            : Environment.ExpandEnvironmentVariables(value);

        Environment.SetEnvironmentVariable(name, expandedValue, EnvironmentVariableTarget.Process);
        return true;
    }
}
