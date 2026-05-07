using CSharpFar.Core.Highlighting;

namespace CSharpFar.Core.FileMasks;

/// <summary>
/// Parses Far Manager-style mask expressions.
/// Handles group expansion, %PATHEXT%, include/exclude splitting, and token splitting.
/// </summary>
internal static class FarMaskParser
{
    private const string PathExtMacro = "%PATHEXT%";

    // ── Group + PATHEXT expansion ────────────────────────────────────────────

    /// <summary>
    /// Expands &lt;group&gt; references and %PATHEXT% in an expression.
    /// 'visiting' tracks currently-expanding groups to detect and break cycles.
    /// </summary>
    public static string ExpandGroupsAndPathext(
        string expression,
        IReadOnlyDictionary<string, MaskGroup> groups,
        HashSet<string> visiting,
        string pathExt)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < expression.Length)
        {
            if (expression[i] == '<')
            {
                int j = expression.IndexOf('>', i + 1);
                if (j > i)
                {
                    string name = expression[(i + 1)..j];
                    if (!visiting.Contains(name) && groups.TryGetValue(name, out var group))
                    {
                        visiting.Add(name);
                        string inner = ExpandGroupsAndPathext(group.MaskExpression, groups, visiting, pathExt);
                        visiting.Remove(name);
                        sb.Append(inner);
                    }
                    // cyclic or unknown group → empty (produces no matches)
                    i = j + 1;
                }
                else
                {
                    sb.Append(expression[i++]);
                }
            }
            else if (expression[i] == '%' &&
                     expression[i..].StartsWith(PathExtMacro, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(pathExt))
                {
                    var parts = pathExt.Split(';')
                        .Select(e => e.Trim())
                        .Where(e => e.Length > 0)
                        .Select(e => "*" + e.ToLowerInvariant());
                    sb.Append(string.Join(",", parts));
                }
                i += PathExtMacro.Length;
            }
            else
            {
                sb.Append(expression[i++]);
            }
        }
        return sb.ToString();
    }

    // ── Include / exclude split ───────────────────────────────────────────────

    /// <summary>
    /// Splits on the first unquoted, non-regex | character.
    /// Returns (include, exclude). Empty include can be normalized to "*".
    /// A second | in the exclude part is truncated (tolerant parsing).
    /// </summary>
    public static (string Include, string Exclude) SplitOnFirstPipe(
        string expression,
        bool emptyIncludeMatchesAll = true)
    {
        bool inQuote = false, inRegex = false;
        char prev = '\0';
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (inQuote)
            {
                if (c == '"') inQuote = false;
                prev = c;
                continue;
            }
            if (inRegex)
            {
                if (c == '/' && prev != '\\') inRegex = false;
                prev = c;
                continue;
            }
            if (c == '"')  { inQuote = true; prev = c; continue; }
            if (c == '/')  { inRegex = true; prev = c; continue; }
            if (c == '|')
            {
                string include = expression[..i].Trim();
                string rest    = expression[(i + 1)..].Trim();
                // Truncate at second | (tolerant parsing)
                int second = FindPipeUnquoted(rest);
                string exclude = second >= 0 ? rest[..second].Trim() : rest;
                return (include.Length == 0 && emptyIncludeMatchesAll ? "*" : include, exclude);
            }
            prev = c;
        }
        return (expression.Trim(), string.Empty);
    }

    private static int FindPipeUnquoted(string s)
    {
        bool inQ = false, inR = false;
        char p = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQ) { if (c == '"') inQ = false; p = c; continue; }
            if (inR) { if (c == '/' && p != '\\') inR = false; p = c; continue; }
            if (c == '"') { inQ = true; p = c; continue; }
            if (c == '/') { inR = true; p = c; continue; }
            if (c == '|') return i;
            p = c;
        }
        return -1;
    }

    // ── Token split ──────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a mask side (include or exclude) into individual tokens.
    /// Separators are , and ; outside of "quoted" tokens and /regex/ tokens.
    /// </summary>
    public static IReadOnlyList<string> SplitTokens(string expression)
    {
        var tokens = new List<string>();
        var sb     = new System.Text.StringBuilder();
        bool inQuote = false;
        bool inRegex = false;
        char prev    = '\0';

        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];

            if (inQuote)
            {
                sb.Append(c);
                if (c == '"') inQuote = false;
                prev = c;
                continue;
            }

            if (inRegex)
            {
                sb.Append(c);
                if (c == '/' && prev != '\\')
                {
                    inRegex = false;
                    // Consume optional flags (letters immediately after closing /)
                    while (i + 1 < expression.Length && char.IsLetter(expression[i + 1]))
                    {
                        i++;
                        sb.Append(expression[i]);
                    }
                }
                prev = c;
                continue;
            }

            if (c == '"')
            {
                inQuote = true;
                sb.Append(c);
                prev = c;
                continue;
            }

            // Regex token starts with / at the start of a new token (sb is empty / all whitespace)
            if (c == '/' && IsTokenStart(sb))
            {
                sb.Clear();
                inRegex = true;
                sb.Append(c);
                prev = c;
                continue;
            }

            if (c == ',' || c == ';')
            {
                FlushToken(sb, tokens);
                prev = '\0';
                continue;
            }

            sb.Append(c);
            prev = c;
        }
        FlushToken(sb, tokens);
        return tokens;
    }

    private static bool IsTokenStart(System.Text.StringBuilder sb) =>
        sb.Length == 0 || sb.ToString().Trim().Length == 0;

    private static void FlushToken(System.Text.StringBuilder sb, List<string> tokens)
    {
        string t = sb.ToString().Trim();
        if (t.Length > 0) tokens.Add(t);
        sb.Clear();
    }
}
