using CSharpFar.Core.Highlighting;

namespace CSharpFar.Core.FileMasks;

/// <summary>
/// Far Manager-compatible file mask matcher.
/// Supports *, ?, [ranges], comma/semicolon lists, | exclude, &lt;groups&gt;,
/// /regex/flags, %PATHEXT%, *.* normalization, and cycle-safe group expansion.
/// </summary>
public sealed class FarMaskMatcher : IFileMaskMatcher
{
    private readonly string _pathExt;
    private readonly Dictionary<MaskCacheKey, CompiledMaskExpression> _cache = new();

    public FarMaskMatcher(string? pathExt = null)
    {
        _pathExt = pathExt ?? Environment.GetEnvironmentVariable("PATHEXT") ?? string.Empty;
    }

    public bool IsMatch(
        string maskExpression,
        string fileName,
        IReadOnlyDictionary<string, MaskGroup> groups)
    {
        return IsMatch(maskExpression, fileName, groups, caseSensitive: false);
    }

    public bool IsMatch(
        string maskExpression,
        string fileName,
        IReadOnlyDictionary<string, MaskGroup> groups,
        bool caseSensitive)
    {
        var compiled = GetOrCompile(maskExpression, groups, caseSensitive);
        return compiled.IsMatch(fileName);
    }

    // ── private ───────────────────────────────────────────────────────────────

    private CompiledMaskExpression GetOrCompile(
        string expression,
        IReadOnlyDictionary<string, MaskGroup> groups,
        bool caseSensitive)
    {
        var key = new MaskCacheKey(expression, _pathExt, BuildGroupsFingerprint(groups), caseSensitive);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var compiled = Compile(expression, groups, caseSensitive);
        _cache[key] = compiled;
        return compiled;
    }

    private CompiledMaskExpression Compile(
        string expression,
        IReadOnlyDictionary<string, MaskGroup> groups,
        bool caseSensitive)
    {
        var (rawInclude, _) = FarMaskParser.SplitOnFirstPipe(
            expression,
            emptyIncludeMatchesAll: false);

        // 1. Expand <group> references and %PATHEXT%
        string expanded = FarMaskParser.ExpandGroupsAndPathext(
            expression,
            groups,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            _pathExt);

        // 2. Split on first |
        var (includeStr, excludeStr) = FarMaskParser.SplitOnFirstPipe(
            expanded,
            emptyIncludeMatchesAll: rawInclude.Length == 0);

        // 3. Tokenize
        var includeTokens = FarMaskParser.SplitTokens(includeStr);
        var excludeTokens = string.IsNullOrWhiteSpace(excludeStr)
            ? []
            : FarMaskParser.SplitTokens(excludeStr);

        // 4. Compile tokens to Regex
        var include = includeTokens
            .Select(token => CompiledMaskExpression.CompileToken(token, caseSensitive))
            .OfType<System.Text.RegularExpressions.Regex>()
            .ToArray();

        var exclude = excludeTokens
            .Select(token => CompiledMaskExpression.CompileToken(token, caseSensitive))
            .OfType<System.Text.RegularExpressions.Regex>()
            .ToArray();

        return new CompiledMaskExpression(include, exclude);
    }

    private static string BuildGroupsFingerprint(IReadOnlyDictionary<string, MaskGroup> groups)
    {
        if (groups.Count == 0) return string.Empty;

        return string.Join("\u001f", groups
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Concat(
                g.Key.ToUpperInvariant(),
                "\u001e",
                g.Value.MaskExpression)));
    }

    private readonly record struct MaskCacheKey(
        string Expression,
        string PathExt,
        string GroupsFingerprint,
        bool CaseSensitive);
}
