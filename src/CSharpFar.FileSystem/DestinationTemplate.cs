using System.Globalization;

namespace CSharpFar.FileSystem;

internal sealed class DestinationTemplate
{
    private readonly IReadOnlyList<Segment> _segments;

    private DestinationTemplate(IReadOnlyList<Segment> segments) => _segments = segments;

    public static DestinationTemplate Parse(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        if (template.IndexOfAny(['*', '?']) >= 0)
            throw new IOException("Destination templates cannot contain FAR wildcard characters.");

        var segments = new List<Segment>();
        var literal = new System.Text.StringBuilder();
        for (int index = 0; index < template.Length; index++)
        {
            char current = template[index];
            if (current is '{' or '}')
            {
                if (index + 1 < template.Length && template[index + 1] == current)
                {
                    literal.Append(current);
                    index++;
                    continue;
                }

                if (current == '}')
                    throw new IOException("Destination template contains an unmatched '}'.");

                AddLiteral(segments, literal);
                int end = template.IndexOf('}', index + 1);
                if (end < 0)
                    throw new IOException("Destination template contains an unmatched '{'.");

                string token = template[(index + 1)..end];
                segments.Add(ParseToken(token));
                index = end;
                continue;
            }
            literal.Append(current);
        }

        AddLiteral(segments, literal);
        return new DestinationTemplate(segments);
    }

    public string Evaluate(DestinationTemplateContext context)
    {
        var result = new System.Text.StringBuilder();
        foreach (Segment segment in _segments)
        {
            string value = segment.Evaluate(context);
            if (segment.IsDynamic)
                ValidateTokenFragment(value, context.DestinationPathSeparators);
            result.Append(value);
        }
        return result.ToString();
    }

    private static void ValidateTokenFragment(string value, IReadOnlyCollection<char> destinationPathSeparators)
    {
        if (value.IndexOfAny(destinationPathSeparators.ToArray()) >= 0)
            throw new IOException("Destination template token result cannot contain a path separator.");
    }

    private static Segment ParseToken(string token) => token switch
    {
        "name" => new NameSegment(),
        "ext" => new ExtensionSegment(),
        _ when token.StartsWith("modified:", StringComparison.Ordinal) && token.Length > "modified:".Length
            => new ModifiedSegment(token["modified:".Length..]),
        _ when token.StartsWith("modified", StringComparison.Ordinal)
            => throw new IOException($"Destination template token '{{{token}}}' requires a format."),
        _ => throw new IOException($"Unknown destination template token '{{{token}}}'."),
    };

    private static void AddLiteral(List<Segment> segments, System.Text.StringBuilder literal)
    {
        if (literal.Length == 0)
            return;
        segments.Add(new LiteralSegment(literal.ToString()));
        literal.Clear();
    }

    private abstract record Segment
    {
        public virtual bool IsDynamic => false;
        public abstract string Evaluate(DestinationTemplateContext context);
    }

    private sealed record LiteralSegment(string Value) : Segment
    {
        public override string Evaluate(DestinationTemplateContext context) => Value;
    }

    private sealed record NameSegment : Segment
    {
        public override bool IsDynamic => true;
        public override string Evaluate(DestinationTemplateContext context) => context.IsDirectory ? context.Name : GetNameWithoutExtension(context.Name);
    }

    private sealed record ExtensionSegment : Segment
    {
        public override bool IsDynamic => true;
        public override string Evaluate(DestinationTemplateContext context) => context.IsDirectory ? string.Empty : GetExtension(context.Name);
    }

    private sealed record ModifiedSegment(string Format) : Segment
    {
        public override bool IsDynamic => true;
        public override string Evaluate(DestinationTemplateContext context)
        {
            try
            {
                string value = context.LastWriteTime.ToString(Format, CultureInfo.InvariantCulture);
                return value;
            }
            catch (FormatException ex)
            {
                throw new IOException($"Invalid modified timestamp format '{Format}'.", ex);
            }
        }
    }

    private static string GetNameWithoutExtension(string name)
    {
        string extension = GetExtension(name);
        return extension.Length == 0 ? name : name[..^extension.Length];
    }

    private static string GetExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[dot..];
    }
}

internal readonly record struct DestinationTemplateContext(
    string Name,
    bool IsDirectory,
    DateTime LastWriteTime,
    IReadOnlyCollection<char> DestinationPathSeparators);
