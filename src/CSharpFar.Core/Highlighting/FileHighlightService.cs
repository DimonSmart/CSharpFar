using CSharpFar.Core.FileMasks;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Highlighting;

/// <summary>
/// Evaluates file highlight rules for a given file and row state.
/// Rules are pre-sorted by Order, then Id at construction time.
/// </summary>
public sealed class FileHighlightService : IFileHighlightService
{
    private readonly IReadOnlyList<FileHighlightRule>          _rules;
    private readonly IReadOnlyDictionary<string, MaskGroup>   _groups;
    private readonly FarMaskMatcher                           _matcher;

    public FileHighlightService(
        IReadOnlyList<FileHighlightRule>        rules,
        IReadOnlyDictionary<string, MaskGroup>  groups,
        string? pathExt = null)
    {
        _rules   = [.. rules.OrderBy(r => r.Order).ThenBy(r => r.Id)];
        _groups  = groups;
        _matcher = new FarMaskMatcher(pathExt);
    }

    public HighlightResult GetHighlight(FilePanelItem item, FileRowState state)
    {
        FileHighlightColor? colorOverride = null;
        string?             markText      = null;
        List<string>?       matchedIds    = null;

        foreach (var rule in _rules)
        {
            if (!rule.Enabled) continue;

            if (!AttributesMatch(item.Attributes, rule.RequiredAttributes, rule.ForbiddenAttributes))
                continue;

            if (rule.UseMask && !_matcher.IsMatch(rule.MaskExpression, item.Name, _groups))
                continue;

            // Rule matched
            var stateColor = rule.Colors.GetColor(state);
            colorOverride  = MergeColor(colorOverride, stateColor);

            if (rule.MarkText != null)
                markText = rule.MarkText;

            (matchedIds ??= []).Add(rule.Id);

            if (!rule.ContinueProcessing) break;
        }

        if (matchedIds == null)
            return new HighlightResult();

        return new HighlightResult
        {
            ColorOverride  = colorOverride,
            MatchedRuleIds = matchedIds,
            MarkText       = markText,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static FileHighlightColor? MergeColor(FileHighlightColor? existing, FileHighlightColor? @new)
    {
        if (@new == null)      return existing;
        if (existing == null)  return @new;
        return new FileHighlightColor(
            @new.Foreground ?? existing.Foreground,
            @new.Background ?? existing.Background);
    }

    private static bool AttributesMatch(
        FileAttributes itemAttrs,
        FileAttributes required,
        FileAttributes forbidden)
    {
        return (required == 0 || (itemAttrs & required) == required)
            && (forbidden == 0 || (itemAttrs & forbidden) == 0);
    }
}
