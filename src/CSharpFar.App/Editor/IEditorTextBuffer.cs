namespace CSharpFar.App.Editor;

public interface IEditorTextBuffer
{
    int LineCount { get; }
    string GetLine(int lineIndex);
    string? GetLineEnding(int lineIndex);
    EditorPosition NormalizePosition(EditorPosition position);
    EditorPosition End { get; }
    string GetText(EditorDocumentFormat format);
    string GetTextInRange(EditorPosition start, EditorPosition end);
    void Replace(EditorPosition start, EditorPosition end, string replacementText, EditorLineEnding insertedLineEnding);
}
