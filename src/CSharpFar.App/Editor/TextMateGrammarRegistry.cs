using CSharpFar.Core.Models;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace CSharpFar.App.Editor;

public sealed class TextMateGrammarRegistry
{
    private readonly Dictionary<string, IGrammar> _loadedGrammars = new(StringComparer.Ordinal);

    public TextMateGrammarRegistry(AppSettings.EditorSettings settings, string requestedTheme)
    {
        ThemeName themeName = TextMateThemeMapper.ResolveThemeName(
            requestedTheme,
            out string resolvedThemeName,
            out string? themeFallbackReason);

        Options = new RegistryOptions(themeName);
        LoadCustomDirectory(settings.SyntaxUserGrammarsPath);
        LoadCustomDirectory(settings.SyntaxUserThemesPath);

        Registry = new Registry(Options);
        ResolvedThemeName = resolvedThemeName;
        ThemeFallbackReason = themeFallbackReason;
    }

    public RegistryOptions Options { get; }
    public Registry Registry { get; }
    public string ResolvedThemeName { get; }
    public string? ThemeFallbackReason { get; }
    public IReadOnlyList<string> LoadDiagnostics => _loadDiagnostics;

    private readonly List<string> _loadDiagnostics = [];

    public bool TryLoadGrammar(
        EditorSyntaxLanguage language,
        out IGrammar? grammar,
        out string? error)
    {
        if (_loadedGrammars.TryGetValue(language.ScopeName, out grammar))
        {
            error = null;
            return true;
        }

        try
        {
            grammar = Registry.LoadGrammar(language.ScopeName);
            if (grammar is null)
            {
                error = $"Grammar not found: {language.ScopeName}";
                return false;
            }

            _loadedGrammars[language.ScopeName] = grammar;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            grammar = null;
            error = ex.Message;
            return false;
        }
    }

    private void LoadCustomDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Options.LoadFromLocalDir(path, true);
        }
        catch (Exception ex)
        {
            _loadDiagnostics.Add($"{path}: {ex.Message}");
        }
    }
}
