namespace CSharpFar.Core.Highlighting;

public sealed record FileHighlightRule
{
    public required string             Id                  { get; init; }
    public required string             DisplayName         { get; init; }

    public bool                        Enabled             { get; init; } = true;
    public int                         Order               { get; init; }

    public bool                        UseMask             { get; init; } = true;
    public string                      MaskExpression      { get; init; } = "*";

    public FileAttributes              RequiredAttributes  { get; init; }
    public FileAttributes              ForbiddenAttributes { get; init; }

    public bool                        ContinueProcessing  { get; init; }
    public string?                     MarkText            { get; init; }

    public required FileHighlightColors Colors             { get; init; }
}
