using CSharpFar.Core.Models;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

public sealed class EditorSyntaxHighlightRequest
{
    public required string FilePath { get; init; }
    public required IEditorTextBuffer Buffer { get; init; }
    public required long DocumentRevision { get; init; }
    public required int FirstLineIndex { get; init; }
    public required int LineCount { get; init; }
    public required AppSettings.EditorSettings Settings { get; init; }
    public required EditorSyntaxHighlightCache Cache { get; init; }
    public required ConsolePalette Palette { get; init; }
    public required CellStyle BaseStyle { get; init; }
    public required bool IsEnabledForSession { get; init; }
    public required string SessionLanguage { get; init; }
    public required string SessionTheme { get; init; }
}
