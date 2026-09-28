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

        Options = CreateOptions(settings, themeName, _loadDiagnostics);
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

    internal static RegistryOptions CreateOptions(
        AppSettings.EditorSettings settings,
        ThemeName themeName,
        ICollection<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var options = new RegistryOptions(themeName);
        LoadCustomDirectory(options, settings.SyntaxUserGrammarsPath, diagnostics);
        LoadCustomDirectory(options, settings.SyntaxUserThemesPath, diagnostics);
        return options;
    }

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

    private static void LoadCustomDirectory(
        RegistryOptions options,
        string? path,
        ICollection<string>? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            options.LoadFromLocalDir(path, true);
        }
        catch (Exception ex)
        {
            diagnostics?.Add($"{path}: {ex.Message}");
        }
    }
}
