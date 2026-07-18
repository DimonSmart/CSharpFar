namespace CSharpFar.Core.Highlighting;

/// <summary>
/// Named group of file masks, e.g. "arc" or "exec".
/// Name is stored without angle brackets: "arc", not "&lt;arc&gt;".
/// Group name lookup is case-insensitive.
/// </summary>
public sealed record MaskGroup
{
    public required string Name { get; init; }
    public required string MaskExpression { get; init; }
    public bool IsBuiltIn { get; init; }
}
