using CSharpFar.Core.Models;
using TextMateSharp.Grammars;

namespace CSharpFar.App.Editor;

internal sealed class TextMateEditorSyntaxCatalog : IEditorSyntaxCatalog
{
    private readonly TextMateLanguageSelector _languageSelector;

    public TextMateEditorSyntaxCatalog(AppSettings.EditorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = TextMateGrammarRegistry.CreateOptions(settings, ThemeName.DarkPlus);
        _languageSelector = new TextMateLanguageSelector(options);
        Languages = _languageSelector.AvailableLanguages
            .Select(language => new EditorSyntaxLanguageCatalogEntry(
                language,
                _languageSelector.GetAliases(language)))
            .ToArray();

        Themes = Enum.GetValues<ThemeName>()
            .Select(theme => new EditorSyntaxTheme(TextMateThemeMapper.DisplayName(theme)))
            .ToArray();
    }

    public IReadOnlyList<EditorSyntaxLanguageCatalogEntry> Languages { get; }

    public EditorSyntaxLanguage? ResolveLanguage(string requestedLanguage) =>
        _languageSelector.ResolveLanguage(requestedLanguage);

    public IReadOnlyList<EditorSyntaxTheme> Themes { get; }

    public EditorSyntaxTheme ResolveTheme(string requestedTheme)
    {
        _ = TextMateThemeMapper.ResolveThemeName(
            requestedTheme,
            out string resolvedThemeName,
            out _);

        return Themes.First(theme =>
            string.Equals(theme.Name, resolvedThemeName, StringComparison.Ordinal));
    }
}
