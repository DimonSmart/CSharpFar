using System.Text;

namespace CSharpFar.App.UserMenu;

internal sealed record CommandTemplateParameter(string Name, string? DefaultValue);
internal sealed record CommandTemplateSegment(string Literal, string? Name, int Start, int End);

internal sealed class CommandTemplate(
    string original,
    IReadOnlyList<CommandTemplateParameter> parameters,
    IReadOnlyList<CommandTemplateSegment> segments)
{
    public string Original { get; } = original;
    public IReadOnlyList<CommandTemplateParameter> Parameters { get; } = parameters;
    internal IReadOnlyList<CommandTemplateSegment> Segments { get; } = segments;

    public string Render(Func<string, string> expandLiteral, Func<string, int, int, string> expandParameter)
    {
        var result = new StringBuilder();
        foreach (CommandTemplateSegment segment in Segments)
            result.Append(segment.Name is null
                ? expandLiteral(segment.Literal)
                : expandParameter(segment.Name, segment.Start, segment.End));
        return result.ToString();
    }
}

internal static class CommandTemplateParser
{
    public static CommandTemplate Parse(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var segments = new List<CommandTemplateSegment>();
        var parameters = new List<CommandTemplateParameter>();
        var byName = new Dictionary<string, CommandTemplateParameter>(StringComparer.Ordinal);
        var literal = new StringBuilder();
        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            segments.Add(new CommandTemplateSegment(literal.ToString(), null, 0, 0));
            literal.Clear();
        }

        for (int i = 0; i < command.Length;)
        {
            if (command.AsSpan(i).StartsWith("{{input:", StringComparison.Ordinal))
            {
                int end = command.IndexOf("}}", i + 8, StringComparison.Ordinal);
                if (end < 0 || command.AsSpan(i + 8, end - i - 8).Contains('{'))
                    throw Invalid(i, "Unterminated escaped input placeholder");
                literal.Append(command.AsSpan(i + 1, end - i));
                i = end + 2;
                continue;
            }
            if (!command.AsSpan(i).StartsWith("{input:", StringComparison.Ordinal))
            {
                literal.Append(command[i++]);
                continue;
            }

            int closing = command.IndexOf('}', i + 7);
            if (closing < 0) throw Invalid(i, "Unterminated input placeholder");
            ReadOnlySpan<char> content = command.AsSpan(i + 7, closing - i - 7);
            if (content.IndexOfAny('{', '}') >= 0)
                throw Invalid(i, "Nested or malformed input placeholder");
            int equals = content.IndexOf('=');
            string name = (equals < 0 ? content : content[..equals]).ToString().Trim();
            if (name.Length == 0) throw Invalid(i, "Input parameter name cannot be empty");
            string? defaultValue = equals < 0 ? null : content[(equals + 1)..].ToString();

            if (byName.TryGetValue(name, out CommandTemplateParameter? existing))
            {
                if (existing.DefaultValue != defaultValue)
                    throw Invalid(i, $"Conflicting defaults for input parameter '{name}'");
            }
            else
            {
                var parameter = new CommandTemplateParameter(name, defaultValue);
                byName.Add(name, parameter);
                parameters.Add(parameter);
            }

            FlushLiteral();
            segments.Add(new CommandTemplateSegment(string.Empty, name, i, closing + 1));
            i = closing + 1;
        }
        FlushLiteral();
        return new CommandTemplate(command, parameters, segments);
    }

    private static FormatException Invalid(int offset, string message) =>
        new($"{message} at position {offset + 1}.");
}
