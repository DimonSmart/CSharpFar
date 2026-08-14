namespace CSharpFar.App.CommandLine;

internal static class EnvironmentVariableCommandParser
{
    public static bool TryParseAssignment(string command, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        string trimmed = command.TrimStart();
        int commandLength = ReadCommandWordLength(trimmed);
        if (commandLength == 0 || !string.Equals(trimmed[..commandLength], "set", StringComparison.OrdinalIgnoreCase))
            return false;

        string assignment = trimmed[commandLength..].TrimStart();
        if (assignment.Length == 0 || IsSpecialSetCommand(assignment))
            return false;

        if (assignment[0] == '"')
        {
            if (assignment.Length < 2 || assignment[^1] != '"')
                return false;

            assignment = assignment[1..^1];
        }
        else if (ContainsShellCommandSeparator(assignment))
        {
            return false;
        }

        int separator = assignment.IndexOf('=');
        if (separator <= 0)
            return false;

        name = assignment[..separator];
        value = assignment[(separator + 1)..];
        return true;
    }

    private static int ReadCommandWordLength(string command)
    {
        int index = 0;
        while (index < command.Length && !char.IsWhiteSpace(command[index]))
            index++;
        return index;
    }

    private static bool IsSpecialSetCommand(string command) =>
        command.Length >= 2 &&
        command[0] == '/' &&
        (command[1] is 'a' or 'A' or 'p' or 'P') &&
        (command.Length == 2 || char.IsWhiteSpace(command[2]));

    private static bool ContainsShellCommandSeparator(string command) =>
        command.IndexOfAny(['&', '|', '<', '>']) >= 0;
}
