namespace CSharpFar.App.Editor;

public sealed class EditorDocument
{
    public EditorDocument(IEditorTextBuffer buffer, EditorDocumentFormat format)
    {
        Buffer = buffer;
        Format = format;
    }

    public IEditorTextBuffer Buffer { get; }
    public EditorDocumentFormat Format { get; private set; }
    public long Revision { get; private set; }
    public long CleanRevision { get; private set; }
    public bool IsDirty => Revision != CleanRevision;

    public void SetFormat(EditorDocumentFormat format) => Format = format;

    public void MarkClean() => CleanRevision = Revision;

    internal void Replace(EditorTextChange change, EditorLineEnding insertedLineEnding)
    {
        Buffer.Replace(change.Start, change.End, change.NewText, insertedLineEnding);
        Revision++;
    }

    internal void RestoreRevision(long revision) => Revision = revision;
}
