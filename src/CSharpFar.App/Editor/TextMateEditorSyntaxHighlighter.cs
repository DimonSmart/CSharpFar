using CSharpFar.Console.Models;
using TextMateSharp.Grammars;

namespace CSharpFar.App.Editor;

public sealed class TextMateEditorSyntaxHighlighter : IEditorSyntaxHighlighter
{
    private readonly Dictionary<int, TextMateLineState> _lineStates = [];
    private TextMateGrammarRegistry? _grammarRegistry;
    private TextMateLanguageSelector? _languageSelector;
    private TextMateThemeMapper? _themeMapper;
    private IGrammar? _grammar;
    private string? _currentLanguageKey;
    private string? _currentThemeKey;
    private bool _disabledForSession;
    private string? _disabledReason;

    public EditorSyntaxHighlightResult Highlight(EditorSyntaxHighlightRequest request)
    {
        if (!request.Settings.SyntaxHighlightingEnabled || !request.IsEnabledForSession)
            return EditorSyntaxHighlightResult.Disabled("Syn:off");

        if (_disabledForSession)
            return EditorSyntaxHighlightResult.Disabled(_disabledReason ?? "Syn:error");

        if (!EnsureRegistry(request))
            return EditorSyntaxHighlightResult.Disabled(_disabledReason ?? "Syn:error");

        var language = _languageSelector!.SelectLanguage(request.FilePath, request.SessionLanguage);
        if (language is null)
        {
            request.Cache.ResetIfChanged("plain", _grammarRegistry!.ResolvedThemeName);
            return EditorSyntaxHighlightResult.Plain("Syn:plain");
        }

        string languageKey = language.ScopeName;
        string themeKey = _grammarRegistry!.ResolvedThemeName;
        request.Cache.ResetIfChanged(languageKey, themeKey);

        if (!EnsureGrammar(language, out string? grammarError))
        {
            request.Cache.Reset("plain", themeKey);
            return EditorSyntaxHighlightResult.Plain($"Syn:plain {grammarError}");
        }

        try
        {
            TokenizeVisibleLines(request, language);
        }
        catch (Exception ex)
        {
            _disabledForSession = true;
            _disabledReason = "Syn:error";
            request.Cache.Reset("disabled", themeKey);
            return new EditorSyntaxHighlightResult(
                [],
                EditorSyntaxDiagnostics.Disabled($"Syn:error {ex.Message}"));
        }

        var spans = CollectVisibleSpans(request);
        string? lastError = _grammarRegistry.ThemeFallbackReason ??
            _grammarRegistry.LoadDiagnostics.FirstOrDefault();
        return new EditorSyntaxHighlightResult(
            spans,
            EditorSyntaxDiagnostics.Active(language.DisplayName, themeKey, "palette", lastError));
    }

    private bool EnsureRegistry(EditorSyntaxHighlightRequest request)
    {
        string themeName = request.SessionTheme;
        if (_grammarRegistry is not null &&
            string.Equals(_currentThemeKey, themeName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            _grammarRegistry = new TextMateGrammarRegistry(request.Settings, themeName);
            _languageSelector = new TextMateLanguageSelector(_grammarRegistry.Options);
            _themeMapper = new TextMateThemeMapper(_grammarRegistry.Registry.GetTheme());
            _grammar = null;
            _currentLanguageKey = null;
            _currentThemeKey = themeName;
            _lineStates.Clear();
            return true;
        }
        catch (Exception ex)
        {
            _disabledForSession = true;
            _disabledReason = $"Syn:error {ex.Message}";
            return false;
        }
    }

    private bool EnsureGrammar(EditorSyntaxLanguage language, out string? error)
    {
        if (_grammar is not null &&
            string.Equals(_currentLanguageKey, language.ScopeName, StringComparison.Ordinal))
        {
            error = null;
            return true;
        }

        if (!_grammarRegistry!.TryLoadGrammar(language, out _grammar, out error))
        {
            _currentLanguageKey = null;
            _lineStates.Clear();
            return false;
        }

        _currentLanguageKey = language.ScopeName;
        _lineStates.Clear();
        return true;
    }

    private void TokenizeVisibleLines(EditorSyntaxHighlightRequest request, EditorSyntaxLanguage language)
    {
        if (_grammar is null)
            return;

        int firstLine = Math.Clamp(request.FirstLineIndex, 0, request.Buffer.LineCount - 1);
        int lastLine = Math.Min(request.Buffer.LineCount - 1, firstLine + Math.Max(0, request.LineCount) - 1);
        if (lastLine < firstLine)
            return;

        RemoveInvalidTextMateStates(request.Cache.FirstInvalidLine);

        int startLine = FindTokenizationStart(request, firstLine, lastLine, out IStateStack? ruleStack);
        int maxSynchronousLines = EditorSettingsResolver.ResolveSyntaxMaxSynchronousLines(request.Settings);
        int lastSynchronousLine = Math.Min(lastLine, startLine + maxSynchronousLines - 1);
        var timeout = TimeSpan.FromMilliseconds(
            EditorSettingsResolver.ResolveSyntaxTokenizationTimeoutMs(request.Settings));

        for (int lineIndex = startLine; lineIndex <= lastSynchronousLine; lineIndex++)
        {
            string lineText = request.Buffer.GetLine(lineIndex);
            if (request.Cache.TryGetLineSpans(lineIndex, lineText, out _) &&
                _lineStates.TryGetValue(lineIndex, out var cachedState))
            {
                ruleStack = cachedState.RuleStackAfter;
                continue;
            }

            if (lineText.Length > EditorSettingsResolver.ResolveSyntaxMaxLineLength(request.Settings))
            {
                request.Cache.StoreLineSpans(lineIndex, lineText, [], tokenized: false);
                _lineStates[lineIndex] = new TextMateLineState(
                    StringComparer.Ordinal.GetHashCode(lineText),
                    ruleStack,
                    ruleStack);
                continue;
            }

            IStateStack? before = ruleStack;
            var result = _grammar.TokenizeLine(new LineText(lineText), ruleStack, timeout);
            ruleStack = result.RuleStack;
            IReadOnlyList<EditorColorSpan> spans = MapTokens(lineIndex, lineText, result.Tokens, request.BaseStyle);
            request.Cache.StoreLineSpans(lineIndex, lineText, spans, tokenized: true);
            _lineStates[lineIndex] = new TextMateLineState(
                StringComparer.Ordinal.GetHashCode(lineText),
                before,
                ruleStack);
        }
    }

    private int FindTokenizationStart(
        EditorSyntaxHighlightRequest request,
        int firstLine,
        int lastLine,
        out IStateStack? ruleStack)
    {
        ruleStack = null;
        int startLine = Math.Min(firstLine, request.Cache.FirstInvalidLine);
        for (int lineIndex = startLine - 1; lineIndex >= 0; lineIndex--)
        {
            string previousLine = request.Buffer.GetLine(lineIndex);
            if (request.Cache.TryGetLineSpans(lineIndex, previousLine, out _) &&
                _lineStates.TryGetValue(lineIndex, out var state))
            {
                ruleStack = state.RuleStackAfter;
                startLine = lineIndex + 1;
                break;
            }
        }

        if (firstLine - startLine > EditorSettingsResolver.ResolveSyntaxMaxSynchronousLines(request.Settings))
            startLine = firstLine;

        return Math.Clamp(startLine, 0, lastLine);
    }

    private IReadOnlyList<EditorColorSpan> MapTokens(
        int lineIndex,
        string lineText,
        IReadOnlyList<IToken> tokens,
        CellStyle baseStyle)
    {
        if (tokens.Count == 0)
            return [];

        var spans = new List<EditorColorSpan>();
        foreach (var token in tokens)
        {
            int start = Math.Clamp(token.StartIndex, 0, lineText.Length);
            int end = Math.Clamp(token.EndIndex, start, lineText.Length);
            if (end <= start)
                continue;

            CellStyle style = _themeMapper!.MapScopes(token.Scopes, baseStyle);
            if (style.Equals(baseStyle))
                continue;

            spans.Add(new EditorColorSpan(lineIndex, start, end - start, style));
        }

        return spans;
    }

    private IReadOnlyList<EditorColorSpan> CollectVisibleSpans(EditorSyntaxHighlightRequest request)
    {
        var spans = new List<EditorColorSpan>();
        int firstLine = Math.Clamp(request.FirstLineIndex, 0, request.Buffer.LineCount - 1);
        int lastLine = Math.Min(request.Buffer.LineCount - 1, firstLine + Math.Max(0, request.LineCount) - 1);
        for (int lineIndex = firstLine; lineIndex <= lastLine; lineIndex++)
        {
            string lineText = request.Buffer.GetLine(lineIndex);
            if (request.Cache.TryGetLineSpans(lineIndex, lineText, out var lineSpans))
                spans.AddRange(lineSpans);
        }

        return spans;
    }

    private void RemoveInvalidTextMateStates(int firstInvalidLine)
    {
        foreach (int key in _lineStates.Keys.Where(key => key >= firstInvalidLine).ToArray())
            _lineStates.Remove(key);
    }

    private sealed record TextMateLineState(
        int TextHash,
        IStateStack? RuleStackBefore,
        IStateStack? RuleStackAfter);
}
