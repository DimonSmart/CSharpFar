using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class EditorWordNavigationTests
{
    [Fact]
    public void MoveWordRight_ExtendedSelectionUsesTheOrdinaryMovementDestination()
    {
        var session = CreateSession("Wellcome [wlc] - Full throttle");

        session.MoveWordRight(extendSelection: true);

        Assert.Equal(new EditorPosition(0, 10), session.Cursor);
        Assert.Equal("Wellcome [", session.CopySelection());
    }

    [Fact]
    public void MoveWordRight_ExtendedSelectionUsesTheSameCrossLineDestination()
    {
        var session = CreateSession("alpha beta\n\nnext");
        session.MoveTo(new EditorPosition(0, 6));

        session.MoveWordRight(extendSelection: true);

        Assert.Equal(new EditorPosition(2, 0), session.Cursor);
        Assert.Equal("beta\n\n", session.CopySelection());
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

    [Theory]
    [InlineData("alpha-beta", 0, 6)]
    [InlineData("alpha...beta", 0, 8)]
    [InlineData("alpha\n\nbeta", 2, 0)]
    public void MoveWordRight_HasTheSameDestinationWhenExtendingSelection(string text, int line, int column)
    {
        var move = CreateSession(text);
        var extend = CreateSession(text);

        move.MoveWordRight();
        extend.MoveWordRight(extendSelection: true);

        Assert.Equal(move.Cursor, extend.Cursor);
        Assert.Equal(new EditorPosition(line, column), move.Cursor);
        Assert.Equal(EditorPosition.Start, extend.Selection!.Anchor);
    }

    [Fact]
    public void MoveWordNavigation_UsesLocalBoundariesAcrossEmptyLines()
    {
        var session = CreateSession("alpha\n\nbeta");
        session.MoveTo(new EditorPosition(2, 0));

        session.MoveWordLeft();

        Assert.Equal(new EditorPosition(0, 0), session.Cursor);
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
