using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal enum InputShellDialect { Unix, Cmd, PowerShell }

/// <summary>Quotes interactive values in their original shell context. Raw panel placeholders retain legacy expansion.</summary>
internal static class CommandTemplateRenderer
{
    public static InputShellDialect Dialect(string command, PlatformKind platform)
    {
        string prefix = command.TrimStart();
        int colon = prefix.IndexOf(':');
        if (colon > 0 && (prefix[..colon].Equals("ps", StringComparison.OrdinalIgnoreCase) ||
                          prefix[..colon].Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
                          prefix[..colon].Equals("powershell", StringComparison.OrdinalIgnoreCase)))
            return InputShellDialect.PowerShell;
        return platform == PlatformKind.Windows ? InputShellDialect.Cmd : InputShellDialect.Unix;
    }

    public static void ValidatePlacement(CommandTemplate template, PlatformKind? platform)
    {
        if (platform is { } only)
            ValidateFor(template, Dialect(template.Original, only));
        else
        {
            ValidateFor(template, Dialect(template.Original, PlatformKind.Windows));
            ValidateFor(template, Dialect(template.Original, PlatformKind.Linux));
        }
    }

    private static void ValidateFor(CommandTemplate template, InputShellDialect dialect)
    {
        foreach (CommandTemplateSegment segment in template.Segments)
            if (segment.Name is not null)
                _ = GetQuoteContext(template.Original, segment, dialect);
    }

    public static string Expand(
        CommandTemplate template,
        IReadOnlyDictionary<string, string> values,
        Func<string, string> expandLiteral,
        InputShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(expandLiteral);

        return template.Render(expandLiteral, (name, start, end) =>
        {
            if (!values.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Input parameter '{name}' is required.");
            return Quote(value, GetQuoteContext(template.Original,
                new CommandTemplateSegment(string.Empty, name, start, end), dialect), dialect, name);
        });
    }

    private enum QuoteContext { Bare, Single, Double }

    private static QuoteContext GetQuoteContext(string command, CommandTemplateSegment segment, InputShellDialect dialect)
    {
        int start = segment.Start, end = segment.End;
        QuoteContext context = ScanQuotes(command, start, dialect);
        bool doubleWrapped = start > 0 && end < command.Length &&
            command[start - 1] == '"' && command[end] == '"' &&
            ScanQuotes(command, start - 1, dialect) == QuoteContext.Bare &&
            context == QuoteContext.Double;
        bool singleWrapped = dialect != InputShellDialect.Cmd &&
            start > 0 && end < command.Length &&
            command[start - 1] == '\'' && command[end] == '\'' &&
            ScanQuotes(command, start - 1, dialect) == QuoteContext.Bare &&
            context == QuoteContext.Single;

        if (doubleWrapped) return QuoteContext.Double;
        if (singleWrapped) return QuoteContext.Single;

        bool leftBoundary = start == 0 || char.IsWhiteSpace(command[start - 1]) ||
            command[start - 1] is '=' or '|' or '&' or ';' or '(';
        bool rightBoundary = end == command.Length || char.IsWhiteSpace(command[end]) ||
            command[end] is '|' or '&' or ';' or ')' or '>' or '<';
        if (context == QuoteContext.Bare && leftBoundary && rightBoundary)
            return QuoteContext.Bare;

        throw new FormatException(
            $"Input parameter '{segment.Name}' at position {start + 1} must occupy a whole shell argument or a complete quoted value.");
    }

    private static QuoteContext ScanQuotes(string command, int end, InputShellDialect dialect)
    {
        QuoteContext state = QuoteContext.Bare;
        for (int i = 0; i < end; i++)
        {
            char c = command[i];
            if (dialect == InputShellDialect.Cmd)
            {
                if (c == '^' && i + 1 < end) { i++; continue; }
                if (c == '"') state = state == QuoteContext.Double ? QuoteContext.Bare : QuoteContext.Double;
                continue;
            }

            if (dialect == InputShellDialect.Unix)
            {
                if (state != QuoteContext.Single && c == '\\' && i + 1 < end) { i++; continue; }
            }
            else
            {
                if (state != QuoteContext.Single && c == '`' && i + 1 < end) { i++; continue; }
                if (state == QuoteContext.Single && c == '\'' && i + 1 < end && command[i + 1] == '\'')
                { i++; continue; }
            }

            if (c == '\'' && state != QuoteContext.Double)
                state = state == QuoteContext.Single ? QuoteContext.Bare : QuoteContext.Single;
            else if (c == '"' && state != QuoteContext.Single)
                state = state == QuoteContext.Double ? QuoteContext.Bare : QuoteContext.Double;
        }
        return state;
    }

    private static string Quote(string value, QuoteContext context, InputShellDialect dialect, string name)
    {
        if (value.IndexOfAny('\0', '\r', '\n') >= 0)
            throw new ArgumentException($"Input parameter '{name}' cannot contain line breaks or NUL.");

        if (dialect == InputShellDialect.Cmd)
        {
            // cmd.exe has no lossless quoting rule for embedded quotes, percent/delayed expansion or caret.
            // Reject ambiguous input rather than interpolating partially escaped shell syntax.
            if (value.IndexOfAny(['"', '%', '!', '^']) >= 0)
                throw new ArgumentException(
                    $"Input parameter '{name}' contains characters unsafe for cmd.exe (double quote, %, ! or ^).");
            return context == QuoteContext.Double ? value : "\"" + value + "\"";
        }

        if (dialect == InputShellDialect.PowerShell)
        {
            if (context == QuoteContext.Double)
                return value.Replace("`", "``").Replace("\"", "`\"").Replace("$", "`$");
            string escaped = value.Replace("'", "''");
            return context == QuoteContext.Single ? escaped : "'" + escaped + "'";
        }

        if (context == QuoteContext.Double)
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("$", "\\$").Replace("`", "\\`");

        string single = value.Replace("'", "'\\''");
        return context == QuoteContext.Single ? single : "'" + single + "'";
    }
}
