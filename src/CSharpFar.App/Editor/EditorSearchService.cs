using System.Text.RegularExpressions;

namespace CSharpFar.App.Editor;

public sealed class EditorSearchService
{
    private readonly string _wordSeparators;

    public EditorSearchService(string wordSeparators)
    {
        _wordSeparators = wordSeparators;
    }

    public EditorSearchMatch? Find(EditorSession session, EditorSearchOptions options)
    {
        IReadOnlyList<EditorSearchMatch> matches = FindAll(session, options);
        if (matches.Count == 0)
            return null;

        int cursorOffset = session.PositionToOffset(session.Cursor);
        if (options.SearchBackward)
        {
            return matches
                .Where(match => session.PositionToOffset(match.Start) < cursorOffset)
                .DefaultIfEmpty(matches[^1])
                .Last();
        }

        return matches
            .FirstOrDefault(match => session.PositionToOffset(match.Start) >= cursorOffset, matches[0]);
    }

    public IReadOnlyList<EditorSearchMatch> FindAll(EditorSession session, EditorSearchOptions options)
    {
        if (options.Pattern.Length == 0)
            return [];

        string text = session.FlattenText();
        var matches = new List<EditorSearchMatch>();
        if (options.UseRegex)
        {
            var regexOptions = RegexOptions.CultureInvariant;
            if (!options.CaseSensitive)
                regexOptions |= RegexOptions.IgnoreCase;

            foreach (Match match in Regex.Matches(text, options.Pattern, regexOptions))
            {
                if (!match.Success || match.Length == 0)
                    continue;
                if (options.WholeWords && !IsWholeWord(text, match.Index, match.Index + match.Length))
                    continue;
                matches.Add(new EditorSearchMatch(
                    session.OffsetToPosition(match.Index),
                    session.OffsetToPosition(match.Index + match.Length)));
            }

            return matches;
        }

        StringComparison comparison = options.CaseSensitive
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;
        int index = 0;
        while (index <= text.Length)
        {
            int found = text.IndexOf(options.Pattern, index, comparison);
            if (found < 0)
                break;

            int end = found + options.Pattern.Length;
            if (!options.WholeWords || IsWholeWord(text, found, end))
            {
                matches.Add(new EditorSearchMatch(
                    session.OffsetToPosition(found),
                    session.OffsetToPosition(end)));
            }

            index = Math.Max(end, found + 1);
        }

        return matches;
    }

    private bool IsWholeWord(string text, int start, int end)
    {
        bool before = start == 0 || IsWordSeparator(text[start - 1]);
        bool after = end >= text.Length || IsWordSeparator(text[end]);
        return before && after;
    }

    private bool IsWordSeparator(char ch) =>
        _wordSeparators.IndexOf(ch, StringComparison.Ordinal) >= 0;
}
