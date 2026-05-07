namespace CSharpFar.Core.Highlighting;

/// <summary>
/// Partial or full color override for one file row component.
/// null in a component means "inherit that component from the base row color".
/// </summary>
public sealed record FileHighlightColor(int? Foreground, int? Background);
