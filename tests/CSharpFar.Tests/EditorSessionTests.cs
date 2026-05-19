using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class EditorSessionTests
{
    [Fact]
    public void Buffer_PreservesMixedLineEndings()
    {
        var buffer = EditorTextBuffer.FromText("a\r\nb\nc\rd");
        var format = new EditorDocumentFormat(Encoding.UTF8, false, EditorLineEnding.Mixed, "UTF-8");

        Assert.Equal("a\r\nb\nc\rd", buffer.GetText(format));
    }

    [Fact]
    public void Session_InsertDeleteAndUndoRedo_UpdateDirtyState()
    {
        var session = CreateSession("AB");

        session.InsertText("X");
        Assert.Equal("XAB", session.Document.Buffer.GetLine(0));
        Assert.True(session.Document.IsDirty);

        Assert.True(session.Undo());
        Assert.Equal("AB", session.Document.Buffer.GetLine(0));
        Assert.False(session.Document.IsDirty);

        Assert.True(session.Redo());
        Assert.Equal("XAB", session.Document.Buffer.GetLine(0));
        Assert.True(session.Document.IsDirty);
    }

    [Fact]
    public void Session_BreakLineAndJoin_AreUndoable()
    {
        var session = CreateSession("Hello World");
        for (int i = 0; i < 5; i++)
            session.MoveRight();

        session.BreakLine();
        Assert.Equal("Hello", session.Document.Buffer.GetLine(0));
        Assert.Equal(" World", session.Document.Buffer.GetLine(1));

        session.Undo();
        Assert.Equal(1, session.Document.Buffer.LineCount);
        Assert.Equal("Hello World", session.Document.Buffer.GetLine(0));
    }

    [Fact]
    public void Session_WordNavigation_UsesConfiguredSeparators()
    {
        var settings = new AppSettings.EditorSettings { WordDiv = " -" };
        var session = CreateSession("alpha-beta gamma", settings);

        session.MoveWordRight();
        Assert.Equal(6, session.Cursor.Column);

        session.MoveWordRight();
        Assert.Equal(11, session.Cursor.Column);
    }

    [Fact]
    public void Search_WholeWords_UsesConfiguredSeparators()
    {
        var settings = new AppSettings.EditorSettings { WordDiv = " -" };
        var session = CreateSession("alpha beta alphabet", settings);

        var match = session.Find(new EditorSearchOptions("beta", WholeWords: true));

        Assert.NotNull(match);
        Assert.Equal(new EditorPosition(0, 6), match.Value.Start);
    }

    [Fact]
    public void ShiftCursorSelection_CopiesSelectedRange()
    {
        var session = CreateSession("abcd");

        session.MoveRight(extendSelection: true);
        session.MoveRight(extendSelection: true);

        Assert.Equal("ab", session.CopySelection());
    }

    [Fact]
    public void Cursor_CanMovePastEndOfLine()
    {
        var session = CreateSession("a");

        session.MoveRight();
        session.MoveRight();
        session.MoveRight();

        Assert.Equal(3, session.Cursor.Column);
    }

    [Fact]
    public void LinearCopy_ExcludesVirtualSpaceAfterLineEnd()
    {
        var session = CreateSession("a\nb");

        session.MoveRight();
        session.MoveRight(extendSelection: true);
        session.MoveRight(extendSelection: true);
        session.MoveDown(extendSelection: true);

        Assert.Equal("\nb", session.CopySelection());
    }

    [Fact]
    public void RectangularCopy_ExcludesVirtualSpaceAfterLineEnd()
    {
        var session = CreateSession("abc\nx");
        session.MoveTo(new EditorPosition(0, 1));
        session.SetSelectionMode(EditorSelectionMode.Rectangular);
        session.MoveTo(new EditorPosition(1, 4), extendSelection: true);

        Assert.Equal("bc\n", session.CopySelection());
    }

    [Fact]
    public void InsertText_InVirtualSpacePadsWithRealSpaces()
    {
        var session = CreateSession("a");
        session.MoveTo(new EditorPosition(0, 4));

        session.InsertText("x");

        Assert.Equal("a   x", session.Document.Buffer.GetLine(0));
        Assert.Equal(5, session.Cursor.Column);
    }

    [Fact]
    public void Search_CaseSensitive_ControlsMatching()
    {
        var session = CreateSession("Alpha alpha");

        var insensitive = session.Find(new EditorSearchOptions("alpha", CaseSensitive: false));
        var sensitive = session.Find(new EditorSearchOptions("alpha", CaseSensitive: true));

        Assert.Equal(new EditorPosition(0, 0), insensitive!.Value.Start);
        Assert.Equal(new EditorPosition(0, 6), sensitive!.Value.Start);
    }

    [Fact]
    public void CommandBindings_MapFindRepeatDirections()
    {
        var shift = EditorCommandBindings.ForModifiers(ConsoleModifiers.Shift).Single(item => item.KeyNumber == 7);
        var alt = EditorCommandBindings.ForModifiers(ConsoleModifiers.Alt).Single(item => item.KeyNumber == 7);

        Assert.Equal("Next", shift.Label);
        Assert.Equal("Prev", alt.Label);
    }

    private static EditorSession CreateSession(
        string text,
        AppSettings.EditorSettings? settings = null)
    {
        settings ??= new AppSettings.EditorSettings();
        var format = new EditorDocumentFormat(Encoding.UTF8, false, EditorLineEnding.Lf, "UTF-8");
        var document = new EditorDocument(EditorTextBuffer.FromText(text), format);
        document.MarkClean();
        return new EditorSession("test.txt", document, settings, readOnly: false);
    }
}
