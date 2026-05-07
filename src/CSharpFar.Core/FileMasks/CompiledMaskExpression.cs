using System.Text.RegularExpressions;

namespace CSharpFar.Core.FileMasks;

/// <summary>
/// Pre-compiled include/exclude regex lists for a mask expression.
/// </summary>
internal sealed class CompiledMaskExpression
{
    private readonly Regex[] _include;
    private readonly Regex[] _exclude;

    public CompiledMaskExpression(Regex[] include, Regex[] exclude)
    {
        _include = include;
        _exclude = exclude;
    }

    public bool IsMatch(string fileName)
    {
        if (_include.Length == 0) return false;

        bool included = false;
        foreach (var r in _include)
            if (r.IsMatch(fileName)) { included = true; break; }
        if (!included) return false;

        foreach (var r in _exclude)
            if (r.IsMatch(fileName)) return false;

        return true;
    }

    // ── Token compilation ────────────────────────────────────────────────────

    /// <summary>Returns null if the token is empty or invalid (no match).</summary>
    public static Regex? CompileToken(string token)
    {
        token = token.Trim();
        if (token.Length == 0) return null;

        // Quoted wildcard: "file,name.txt"
        if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
            return CompileWildcard(token[1..^1]);

        // Regex: /pattern/flags
        if (token.StartsWith('/'))
            return CompileRegex(token);

        return CompileWildcard(token);
    }

    private static Regex CompileWildcard(string pattern)
    {
        // Far: *.* matches everything (same as *)
        if (pattern == "*.*") pattern = "*";

        var sb = new System.Text.StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    break;
                case '?':
                    sb.Append('.');
                    break;
                case '[':
                {
                    // Pass character class through as-is
                    int j = pattern.IndexOf(']', i + 1);
                    if (j > i)
                    {
                        sb.Append(pattern[i..(j + 1)]);
                        i = j;
                    }
                    else
                    {
                        sb.Append("\\[");
                    }
                    break;
                }
                default:
                    sb.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Regex CompileRegex(string token)
    {
        // token = /pattern/flags
        int close = 1;
        char prev = '\0';
        while (close < token.Length)
        {
            if (token[close] == '/' && prev != '\\') break;
            prev = token[close];
            close++;
        }

        string pattern = token[1..close];
        string flags   = close < token.Length ? token[(close + 1)..] : string.Empty;

        var opts = RegexOptions.CultureInvariant;
        if (flags.Contains('i')) opts |= RegexOptions.IgnoreCase;

        try   { return new Regex(pattern, opts); }
        catch { return new Regex("(?!)", opts); } // never matches on invalid regex
    }
}
