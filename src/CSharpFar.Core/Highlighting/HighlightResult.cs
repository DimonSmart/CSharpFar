namespace CSharpFar.Core.Highlighting;

public sealed record HighlightResult
{
    public FileHighlightColor?   ColorOverride  { get; init; }
    public IReadOnlyList<string> MatchedRuleIds { get; init; } = [];
    public string?               MarkText       { get; init; }
}
