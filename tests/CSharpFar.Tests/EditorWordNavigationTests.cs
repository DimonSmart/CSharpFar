using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class EditorWordNavigationTests
{
    [Fact]
    public void MoveWordRight_ExtendedSelectionStopsBeforeSeparators()
    {
        var session = CreateSession("Wellcome [wlc] - Full throttle");

        session.MoveWordRight(extendSelection: true);

        Assert.Equal(new EditorPosition(0, 8), session.Cursor);
        Assert.Equal("Wellcome", session.CopySelection());
    }

    [Fact]
    public void MoveWordRight_ExtendedSelectionStopsAtLineEnd()
    {
        var session = CreateSession("alpha beta\n\nnext");
        session.MoveTo(new EditorPosition(0, 6));

        session.MoveWordRight(extendSelection: true);

        Assert.Equal(new EditorPosition(0, 10), session.Cursor);
        Assert.Equal("beta", session.CopySelection());
    }

    [Fact]
    public void MoveWordRight_ExtendedSelectionCrossesLinesToNextWordOnNextMove()
    {
        var session = CreateSession("alpha beta\n\nnext");
        session.MoveTo(new EditorPosition(0, 6));

        session.MoveWordRight(extendSelection: true);
        session.MoveWordRight(extendSelection: true);

        Assert.Equal(new EditorPosition(2, 4), session.Cursor);
        Assert.Equal("beta\n\nnext", session.CopySelection());
    }

    [Fact]
    public void MoveWordRight_WithoutSelectionStillMovesAcrossLines()
    {
        var session = CreateSession("alpha beta\n\nnext");
        session.MoveTo(new EditorPosition(0, 6));

        session.MoveWordRight();

        Assert.Equal(new EditorPosition(2, 0), session.Cursor);
        Assert.Null(session.Selection);
    }

    private static EditorSession CreateSession(string text)
    {
        var settings = new AppSettings.EditorSettings();
        var format = new EditorDocumentFormat(Encoding.UTF8, false, EditorLineEnding.Lf, "UTF-8");
        var document = new EditorDocument(EditorTextBuffer.FromText(text), format);
        document.MarkClean();
        return new EditorSession("test.txt", document, settings, readOnly: false);
    }
}
