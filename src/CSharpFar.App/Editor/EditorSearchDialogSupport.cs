using System.Text.RegularExpressions;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

internal static class EditorSearchDialogSupport
{
    internal const string CaseSensitiveOption = "case-sensitive";
    internal const string WholeWordsOption = "whole-words";
    internal const string UseRegexOption = "use-regex";
    internal const string SearchBackwardOption = "search-backwards";

    public static IReadOnlyList<SearchOptionLine> CreateOptionLines(EditorSearchOptions? previous) =>
    [
        new(CaseSensitiveOption, "Case sensitive", previous?.CaseSensitive ?? false),
        new(WholeWordsOption, "Whole words", previous?.WholeWords ?? false),
        new(UseRegexOption, "Regular expressions", previous?.UseRegex ?? false),
        new(SearchBackwardOption, "Search backwards", previous?.SearchBackward ?? false),
    ];

    public static EditorSearchOptions CreateOptions(string pattern, Func<string, bool> getOption) =>
        new(
            pattern,
            SearchBackward: getOption(SearchBackwardOption),
            CaseSensitive: getOption(CaseSensitiveOption),
            WholeWords: getOption(WholeWordsOption),
            UseRegex: getOption(UseRegexOption));

    public static string? Validate(EditorSearchOptions options)
    {
        if (options.Pattern.Length == 0)
            return "Search text is required.";

        if (!options.UseRegex)
            return null;

        try
        {
            _ = Regex.IsMatch(string.Empty, options.Pattern, RegexOptions.CultureInvariant);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
