namespace CSharpFar.App.Viewer;

internal readonly record struct ViewerViewportAnchor(long ByteOffset, int VisualSegment = 0)
{
    public int CompareTo(ViewerViewportAnchor other)
    {
        int byteComparison = ByteOffset.CompareTo(other.ByteOffset);
        return byteComparison != 0 ? byteComparison : VisualSegment.CompareTo(other.VisualSegment);
    }
}
