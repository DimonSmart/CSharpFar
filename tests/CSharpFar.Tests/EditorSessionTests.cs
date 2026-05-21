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
    public void SelectAll_SelectsWholeDocumentAndMovesCursorToEnd()
    {
        var session = CreateSession("alpha\nbeta");

        session.MoveTo(new EditorPosition(0, 2));
        session.SelectAll();

        Assert.Equal("alpha\nbeta", session.CopySelection());
        Assert.Equal(new EditorPosition(1, 4), session.Cursor);
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

    [Fact]
    public void DeleteCurrentLine_RemovesLineAndUndoRestoresIt()
    {
        var session = CreateSession("one\ntwo\nthree");
        session.MoveDown();

        Assert.True(session.DeleteCurrentLine());

        Assert.Equal("one\nthree", session.FlattenText());
        Assert.Equal(new EditorPosition(1, 0), session.Cursor);

        Assert.True(session.Undo());
        Assert.Equal("one\ntwo\nthree", session.FlattenText());
        Assert.Equal(new EditorPosition(1, 0), session.Cursor);
    }

    [Fact]
    public void DeleteToLineEnd_RemovesTextAfterCursor()
    {
        var session = CreateSession("alpha beta");
        session.MoveTo(new EditorPosition(0, 6));

        Assert.True(session.DeleteToLineEnd());

        Assert.Equal("alpha ", session.FlattenText());
        Assert.True(session.Undo());
        Assert.Equal("alpha beta", session.FlattenText());
    }

    [Fact]
    public void DeleteWordLeft_UsesConfiguredWordSeparators()
    {
        var settings = new AppSettings.EditorSettings { WordDiv = " -" };
        var session = CreateSession("alpha-beta gamma", settings);
        session.MoveTo(new EditorPosition(0, 10));

        Assert.True(session.DeleteWordLeft());

        Assert.Equal("alpha- gamma", session.FlattenText());
        Assert.Equal(new EditorPosition(0, 6), session.Cursor);
    }

    [Fact]
    public void DeleteWordRight_UsesConfiguredWordSeparators()
    {
        var settings = new AppSettings.EditorSettings { WordDiv = " -" };
        var session = CreateSession("alpha-beta gamma", settings);
        session.MoveTo(new EditorPosition(0, 6));

        Assert.True(session.DeleteWordRight());

        Assert.Equal("alpha-gamma", session.FlattenText());
        Assert.Equal(new EditorPosition(0, 6), session.Cursor);
    }

    [Fact]
    public void DuplicateSelection_InsertsCopyAfterSelection()
    {
        var session = CreateSession("abcd");
        session.MoveRight(extendSelection: true);
        session.MoveRight(extendSelection: true);

        Assert.True(session.DuplicateSelectionOrCurrentLine());

        Assert.Equal("ababcd", session.FlattenText());
        Assert.True(session.Undo());
        Assert.Equal("abcd", session.FlattenText());
    }

    [Fact]
    public void DuplicateCurrentLine_InsertsLineBelow()
    {
        var session = CreateSession("one\ntwo");

        Assert.True(session.DuplicateSelectionOrCurrentLine());

        Assert.Equal("one\none\ntwo", session.FlattenText());
        Assert.Equal(new EditorPosition(1, 0), session.Cursor);
    }

    [Fact]
    public void CopySelectionToCursor_InsertsSelectionWithoutClipboard()
    {
        var session = CreateSession("abcd");
        session.MoveRight(extendSelection: true);
        session.MoveRight(extendSelection: true);

        Assert.True(session.CopySelectionToCursor());

        Assert.Equal("ababcd", session.FlattenText());
        Assert.True(session.Undo());
        Assert.Equal("abcd", session.FlattenText());
    }

    [Fact]
    public void MoveSelectionToCursor_MovesLinearSelection()
    {
        var session = CreateSession("abcd");
        session.SelectRange(new EditorPosition(0, 1), new EditorPosition(0, 3));
        session.MoveTo(new EditorPosition(0, 4), preserveSelection: true);

        Assert.True(session.MoveSelectionToCursor());

        Assert.Equal("adbc", session.FlattenText());
        Assert.True(session.Undo());
        Assert.Equal("abcd", session.FlattenText());
    }

    [Fact]
    public void ConvertSelectionCase_ChangesOnlySelectedText()
    {
        var session = CreateSession("alpha бета");
        session.MoveTo(new EditorPosition(0, 6));
        session.MoveTo(new EditorPosition(0, 10), extendSelection: true);

        Assert.True(session.ConvertSelectionToUppercase());

        Assert.Equal("alpha БЕТА", session.FlattenText());
        Assert.True(session.Undo());
        Assert.Equal("alpha бета", session.FlattenText());
    }

    [Fact]
    public void ShiftSelectedLinesLeftOrRight_ChangesSelectedLines()
    {
        var session = CreateSession(" one\n two\nthree");
        session.SelectRange(new EditorPosition(0, 0), new EditorPosition(1, 2));

        Assert.True(session.ShiftSelectedLinesLeftOrCurrentLine());
        Assert.Equal("one\ntwo\nthree", session.FlattenText());

        Assert.True(session.ShiftSelectedLinesRightOrCurrentLine());
        Assert.Equal(" one\n two\nthree", session.FlattenText());
    }

    [Fact]
    public void RectangularDelete_UndoRestoresOriginalText()
    {
        var session = CreateSession("abcd\nwxyz");
        session.MoveTo(new EditorPosition(0, 1));
        session.SetSelectionMode(EditorSelectionMode.Rectangular);
        session.MoveTo(new EditorPosition(1, 3), extendSelection: true);

        Assert.True(session.DeleteSelection());
        Assert.Equal("ad\nwz", session.FlattenText());

        Assert.True(session.Undo());
        Assert.Equal("abcd\nwxyz", session.FlattenText());
    }

    [Fact]
    public void RectangularCaseChange_UndoRestoresOriginalText()
    {
        var session = CreateSession("abcd\nwxyz");
        session.MoveTo(new EditorPosition(0, 1));
        session.SetSelectionMode(EditorSelectionMode.Rectangular);
        session.MoveTo(new EditorPosition(1, 3), extendSelection: true);

        Assert.True(session.ConvertSelectionToUppercase());
        Assert.Equal("aBCd\nwXYz", session.FlattenText());

        Assert.True(session.Undo());
        Assert.Equal("abcd\nwxyz", session.FlattenText());
    }

    [Fact]
    public void NumberedBookmarks_MoveAfterLineInsertion()
    {
        var session = CreateSession("one\ntwo");
        session.MoveDown();
        session.MoveRight();
        session.SetNumberedBookmark(2);
        session.MoveToDocumentStart();
        session.BreakLine();

        Assert.True(session.MoveToNumberedBookmark(2));

        Assert.Equal(new EditorPosition(2, 1), session.Cursor);
    }

    [Fact]
    public void CommandInventory_HasNoDuplicateImplementedChords()
    {
        var duplicates = EditorKeyboardCommandInventory.Commands
            .Where(command => command.Status == EditorKeyCommandStatus.Implemented)
            .GroupBy(command => (command.Key, command.Modifiers))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Modifiers}+{group.Key.Key}")
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void CommandInventory_UsesFarEditorMappingsForConflictingChords()
    {
        Assert.Contains(EditorKeyboardCommandInventory.Commands, command =>
            command.Key == ConsoleKey.Y &&
            command.Modifiers == ConsoleModifiers.Control &&
            command.CommandId == "delete-line" &&
            command.Status == EditorKeyCommandStatus.Implemented);
        Assert.Contains(EditorKeyboardCommandInventory.Commands, command =>
            command.Key == ConsoleKey.P &&
            command.Modifiers == ConsoleModifiers.Control &&
            command.CommandId == "copy-block" &&
            command.Status == EditorKeyCommandStatus.Implemented);
        Assert.Contains(EditorKeyboardCommandInventory.Commands, command =>
            command.Key == ConsoleKey.Z &&
            command.Modifiers == (ConsoleModifiers.Control | ConsoleModifiers.Shift) &&
            command.CommandId == "redo" &&
            command.Status == EditorKeyCommandStatus.Implemented);
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
