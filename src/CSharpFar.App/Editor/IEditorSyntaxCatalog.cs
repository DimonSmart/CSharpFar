namespace CSharpFar.App.Editor;

internal interface IEditorSyntaxCatalog
{
    IReadOnlyList<EditorSyntaxLanguageCatalogEntry> Languages { get; }

    EditorSyntaxLanguage? ResolveLanguage(string requestedLanguage);

    IReadOnlyList<EditorSyntaxTheme> Themes { get; }

    EditorSyntaxTheme ResolveTheme(string requestedTheme);
}

internal sealed class EditorSyntaxLanguageCatalogEntry
{
    public EditorSyntaxLanguageCatalogEntry(
        EditorSyntaxLanguage language,
        IReadOnlyList<string> aliases)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        Aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        SearchText = string.Join(
            "\n",
            new[] { language.DisplayName, language.Id, language.ScopeName }
                .Concat(aliases)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public EditorSyntaxLanguage Language { get; }

    public IReadOnlyList<string> Aliases { get; }

    public string SearchText { get; }
}
