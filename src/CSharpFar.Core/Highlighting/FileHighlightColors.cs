namespace CSharpFar.Core.Highlighting;

/// <summary>Per-state color overrides for a file highlight rule.</summary>
public sealed record FileHighlightColors
{
    public FileHighlightColor? Normal         { get; init; }
    public FileHighlightColor? Selected       { get; init; }
    public FileHighlightColor? Cursor         { get; init; }
    public FileHighlightColor? SelectedCursor { get; init; }

    public FileHighlightColor? GetColor(FileRowState state) => state switch
    {
        FileRowState.Normal         => Normal,
        FileRowState.Selected       => Selected,
        FileRowState.Cursor         => Cursor,
        FileRowState.SelectedCursor => SelectedCursor,
        _                           => null,
    };
}
