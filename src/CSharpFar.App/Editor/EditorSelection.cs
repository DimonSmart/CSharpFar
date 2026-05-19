namespace CSharpFar.App.Editor;

public sealed record EditorSelection(
    EditorPosition Anchor,
    EditorPosition Active,
    EditorSelectionMode Mode)
{
    public bool IsEmpty => Anchor == Active;

    public (EditorPosition Start, EditorPosition End) OrderedRange =>
        EditorTextBuffer.Compare(Anchor, Active) <= 0 ? (Anchor, Active) : (Active, Anchor);
}
