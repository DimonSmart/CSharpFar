using CSharpFar.Core.Abstractions;

namespace CSharpFar.App.Viewer;

internal sealed class LargeFileViewerOptions
{
    public IReadOnlyList<string> FilePaths { get; init; } = [];
    public int CurrentFileIndex { get; set; } = -1;
    public Action<string>? CurrentFileChanged { get; init; }
    public Action<string>? EditFile { get; init; }
    public ITextClipboard? Clipboard { get; init; }

    public bool HasSiblingFiles =>
        FilePaths.Count > 0 &&
        CurrentFileIndex >= 0 &&
        CurrentFileIndex < FilePaths.Count;
}
