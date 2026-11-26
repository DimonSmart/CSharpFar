using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>Implements FAR Manager's ConvertWildcards name-conversion algorithm.</summary>
internal static class FarWildcardNameTransformer
{
    public static string Transform(string sourceName, string pattern)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.IndexOfAny(['*', '?']) < 0)
            return pattern;

        string sourcePart = sourceName;
        var result = new System.Text.StringBuilder(pattern.Length + sourcePart.Length);
        int sourceIndex = 0;

        for (int patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
        {
            char current = pattern[patternIndex];
            switch (current)
            {
                case '?':
                    if (sourceIndex < sourcePart.Length && sourcePart[sourceIndex] != '.')
                        result.Append(sourcePart[sourceIndex++]);
                    break;

                case '*':
                    if (++patternIndex == pattern.Length)
                    {
                        result.Append(sourcePart, sourceIndex, sourcePart.Length - sourceIndex);
                        break;
                    }

                    char next = pattern[patternIndex];
                    int lastCharacterPosition = next == '?'
                        ? sourcePart.Length
                        : sourcePart.LastIndexOf(next);
                    if (lastCharacterPosition < sourceIndex)
                        lastCharacterPosition = sourcePart.Length;

                    result.Append(sourcePart, sourceIndex, lastCharacterPosition - sourceIndex);
                    if (next != '?')
                        result.Append(next);

                    sourceIndex = Math.Min(lastCharacterPosition + 1, sourcePart.Length);
                    break;

                case '.':
                    result.Append('.');
                    int dot = sourcePart.IndexOf('.', sourceIndex);
                    sourceIndex = dot < 0 ? sourcePart.Length : dot + 1;
                    break;

                default:
                    result.Append(current);
                    if (sourceIndex < sourcePart.Length && sourcePart[sourceIndex] != '.')
                        sourceIndex++;
                    break;
            }
        }

        if (result.Length > 0 && result[^1] == '.')
            result.Length--;

        return result.ToString();
    }
}

internal readonly record struct DestinationPattern(string ParentPath, string NamePattern, bool HasWildcards)
{
    public static DestinationPattern Parse(string destination, IFilePanelSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        // This is a raw user expression; do not normalize wildcards as a path.
        if (source.SourceId == PanelSourceId.Local)
            return ParseLocal(destination);

        int index = destination.LastIndexOfAny(source.PathSeparators.ToArray());
        string parent = index < 0 ? string.Empty : index == 0 ? destination[..1] : destination[..index];
        string name = destination[(index + 1)..];
        if (parent.IndexOfAny(['*', '?']) >= 0)
            throw new IOException("Wildcards are allowed only in the destination file name.");

        return new DestinationPattern(parent, name, name.IndexOfAny(['*', '?']) >= 0);
    }

    public static DestinationPattern ParseLocal(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        string parent = Path.GetDirectoryName(destination) ?? string.Empty;
        string name = Path.GetFileName(destination);

        if (parent.IndexOfAny(['*', '?']) >= 0)
            throw new IOException("Wildcards are allowed only in the destination file name.");

        return new DestinationPattern(parent, name, name.IndexOfAny(['*', '?']) >= 0);
    }

    public string TransformName(string sourceName)
    {
        string name = FarWildcardNameTransformer.Transform(sourceName, NamePattern);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['*', '?']) >= 0)
            throw new IOException("Destination pattern produced an invalid file name.");

        return name;
    }
}
