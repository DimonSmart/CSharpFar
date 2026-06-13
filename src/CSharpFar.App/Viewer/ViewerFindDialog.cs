using System.Text.RegularExpressions;
using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed class ViewerFindDialog
{
    private const string CaseSensitiveOption = "case-sensitive";
    private const string WholeWordsOption = "whole-words";
    private const string UseRegexOption = "use-regex";
    private const string SearchHexOption = "search-hex";

    private readonly ScreenRenderer _screen;

    public ViewerFindDialog(ScreenRenderer screen, ConsolePalette palette)
    {
        _screen = screen;
        _ = palette;
    }

    public ViewerFindDialogResult? Show(ViewerSearchRequest? previous, bool hexMode)
    {
        bool searchHex = previous?.SearchHex ?? hexMode;
        bool useRegex = previous?.UseRegex ?? false;
        if (searchHex)
            useRegex = false;

        var result = new SearchOptionsDialog(_screen).Show(new SearchOptionsDialogOptions
        {
            Title = "Find",
            InitialPattern = previous?.Pattern ?? string.Empty,
            HistoryKey = "Viewer.Find.Pattern",
            Width = 60,
            Options =
            [
                new SearchOptionLine(CaseSensitiveOption, "Case sensitive", previous?.CaseSensitive ?? false),
                new SearchOptionLine(WholeWordsOption, "Whole words", previous?.WholeWords ?? false),
                new SearchOptionLine(UseRegexOption, "Regular expression", useRegex),
                new SearchOptionLine(SearchHexOption, "Hex sequence", searchHex),
            ],
            NormalizeOptions = NormalizeMutuallyExclusiveOptions,
            Validate = Validate,
        });

        return result is null
            ? null
            : new ViewerFindDialogResult(
                result.Pattern,
                result.GetOption(CaseSensitiveOption),
                result.GetOption(WholeWordsOption),
                result.GetOption(UseRegexOption),
                result.GetOption(SearchHexOption));
    }

    private static void NormalizeMutuallyExclusiveOptions(SearchOptionsDialogState state, string changedOptionId)
    {
        if (changedOptionId == UseRegexOption && state.GetOption(UseRegexOption))
            state.SetOption(SearchHexOption, false);
        else if (changedOptionId == SearchHexOption && state.GetOption(SearchHexOption))
            state.SetOption(UseRegexOption, false);
    }

    private static string? Validate(SearchOptionsDialogState state)
    {
        if (state.GetOption(SearchHexOption) &&
            !ViewerSearchEngine.TryParseHexPattern(state.Pattern, out _, out string? error))
        {
            return error;
        }

        if (!state.GetOption(UseRegexOption))
            return null;

        try
        {
            _ = new Regex(state.Pattern);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
