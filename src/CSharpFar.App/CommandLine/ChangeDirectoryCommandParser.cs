namespace CSharpFar.App.CommandLine;

internal static class ChangeDirectoryCommandParser
{
    public static bool TryParseTarget(string command, out string target)
    {
        target = string.Empty;

        string trimmed = command.Trim();
        int commandLength = ReadCommandWordLength(trimmed);
        if (commandLength == 0)
            return false;

        string commandName = trimmed[..commandLength];
        if (!string.Equals(commandName, "cd", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(commandName, "chdir", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = trimmed[commandLength..].TrimStart();
        if (rest.StartsWith("/d", StringComparison.OrdinalIgnoreCase) &&
            (rest.Length == 2 || char.IsWhiteSpace(rest[2])))
        {
            rest = rest[2..].TrimStart();
        }

        if (rest.Length == 0 || ContainsShellCommandSeparator(rest))
            return false;

        target = UnquoteTarget(rest);
        return target.Length > 0;
    }

    private static int ReadCommandWordLength(string command)
    {
        int index = 0;
        while (index < command.Length && !char.IsWhiteSpace(command[index]))
            index++;
        return index;
    }

    private static bool ContainsShellCommandSeparator(string text)
    {
        bool inQuotes = false;
        foreach (char ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && ch is '&' or '|' or '<' or '>')
                return true;
        }

        return false;
    }

    private static string UnquoteTarget(string target)
    {
        string trimmed = target.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1]
            : trimmed;
    }
}
