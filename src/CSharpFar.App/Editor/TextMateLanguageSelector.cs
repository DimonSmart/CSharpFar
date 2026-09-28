using TextMateSharp.Grammars;

namespace CSharpFar.App.Editor;

public sealed class TextMateLanguageSelector
{
    private readonly RegistryOptions _options;
    private readonly Dictionary<string, Language> _languageByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Language> _languageByScope = new(StringComparer.Ordinal);
    private readonly List<(string Extension, Language Language)> _extensionLanguages = [];
    private readonly List<EditorSyntaxLanguage> _availableLanguages = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _aliasesByScope = new(StringComparer.Ordinal);

    public TextMateLanguageSelector(RegistryOptions options)
    {
        _options = options;
        foreach (var language in options.GetAvailableLanguages())
        {
            AddName(language.Id, language);
            foreach (string alias in language.Aliases ?? [])
                AddName(alias, language);

            string? scopeName = ScopeForLanguage(language);
            if (!string.IsNullOrWhiteSpace(scopeName))
                _languageByScope.TryAdd(scopeName, language);

            foreach (string extension in language.Extensions ?? [])
            {
                if (!string.IsNullOrWhiteSpace(extension))
                    _extensionLanguages.Add((extension, language));
            }

            var editorLanguage = ToEditorLanguage(language);
            if (editorLanguage is not null &&
                !_availableLanguages.Any(candidate =>
                    string.Equals(candidate.ScopeName, editorLanguage.ScopeName, StringComparison.Ordinal)))
            {
                _availableLanguages.Add(editorLanguage);
                _aliasesByScope[editorLanguage.ScopeName] = (language.Aliases ?? [])
                    .Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .ToArray();
            }
        }

        _extensionLanguages.Sort((left, right) => right.Extension.Length.CompareTo(left.Extension.Length));
    }

    internal IReadOnlyList<EditorSyntaxLanguage> AvailableLanguages => _availableLanguages;

    internal IReadOnlyList<string> GetAliases(EditorSyntaxLanguage language) =>
        _aliasesByScope.TryGetValue(language.ScopeName, out var aliases)
            ? aliases
            : [];

    internal EditorSyntaxLanguage? ResolveLanguage(string requestedLanguage)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
            return null;

        string language = requestedLanguage.Trim();
        return string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : SelectExplicitLanguage(language);
    }

    public EditorSyntaxLanguage? SelectLanguage(string filePath, string requestedLanguage)
    {
        string language = string.IsNullOrWhiteSpace(requestedLanguage)
            ? "auto"
            : requestedLanguage.Trim();

        if (!string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
            return SelectExplicitLanguage(language);

        return SelectByFileName(filePath);
    }

    private EditorSyntaxLanguage? SelectExplicitLanguage(string language)
    {
        if (_languageByName.TryGetValue(language, out var byName))
            return ToEditorLanguage(byName);

        if (_languageByScope.TryGetValue(language, out var byScope))
            return ToEditorLanguage(byScope);

        if (language.StartsWith("source.", StringComparison.Ordinal) ||
            language.StartsWith("text.", StringComparison.Ordinal))
        {
            return new EditorSyntaxLanguage(language, language, language);
        }

        return null;
    }

    private EditorSyntaxLanguage? SelectByFileName(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        if (string.Equals(fileName, "Dockerfile", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "Containerfile", StringComparison.OrdinalIgnoreCase))
        {
            return SelectExplicitLanguage("dockerfile");
        }

        foreach (var (extension, language) in _extensionLanguages)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return ToEditorLanguage(language);
        }

        string fileExtension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileExtension))
            return null;

        string? scope = _options.GetScopeByExtension(fileExtension);
        return string.IsNullOrWhiteSpace(scope)
            ? null
            : SelectExplicitLanguage(scope);
    }

    private EditorSyntaxLanguage? ToEditorLanguage(Language language)
    {
        string? scope = ScopeForLanguage(language);
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        string id = string.IsNullOrWhiteSpace(language.Id) ? scope : language.Id;
        string displayName = language.Aliases?.FirstOrDefault(alias => !string.IsNullOrWhiteSpace(alias))
            ?? id;
        return new EditorSyntaxLanguage(id, scope, displayName);
    }

    private string? ScopeForLanguage(Language language)
    {
        if (string.IsNullOrWhiteSpace(language.Id))
            return null;

        string? scope = _options.GetScopeByLanguageId(language.Id);
        return string.IsNullOrWhiteSpace(scope) ? null : scope;
    }

    private void AddName(string? name, Language language)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _languageByName.TryAdd(name, language);
    }
}
