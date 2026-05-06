using CSharpFar.App.Editor;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 13: EditorModel — in-memory text editing operations.
/// </summary>
public class EditorModelTests
{
    // ── construction ──────────────────────────────────────────────────────────

    [Fact]
    public void EmptyInput_StartsWithSingleEmptyLine()
    {
        var model = new EditorModel([]);
        Assert.Single(model.Lines);
        Assert.Equal("", model.Lines[0]);
        Assert.False(model.IsDirty);
    }

    // ── InsertChar ────────────────────────────────────────────────────────────

    [Fact]
    public void InsertChar_InsertsAtCursorAndAdvances()
    {
        var model = new EditorModel(["AB"]);
        // cursor at 0 initially
        model.InsertChar('X');
        Assert.Equal("XAB", model.Lines[0]);
        Assert.Equal(1, model.CursorCol);
    }

    [Fact]
    public void InsertChar_SetsDirty()
    {
        var model = new EditorModel(["hello"]);
        Assert.False(model.IsDirty);
        model.InsertChar('!');
        Assert.True(model.IsDirty);
    }

    // ── DeleteBack ────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteBack_DeletesCharBeforeCursor()
    {
        var model = new EditorModel(["AB"]);
        model.MoveToLineEnd(); // cursor at 2
        model.DeleteBack();
        Assert.Equal("A", model.Lines[0]);
        Assert.Equal(1, model.CursorCol);
    }

    [Fact]
    public void DeleteBack_AtLineStartMergesWithPreviousLine()
    {
        var model = new EditorModel(["first", "second"]);
        model.MoveDown(); // row 1, col 0
        model.DeleteBack();
        Assert.Single(model.Lines);
        Assert.Equal("firstsecond", model.Lines[0]);
        Assert.Equal(0, model.CursorRow);
        Assert.Equal(5, model.CursorCol);
    }

    // ── DeleteForward ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteForward_DeletesCharAtCursor()
    {
        var model = new EditorModel(["AB"]);
        // cursor at 0
        model.DeleteForward();
        Assert.Equal("B", model.Lines[0]);
        Assert.Equal(0, model.CursorCol);
    }

    [Fact]
    public void DeleteForward_AtLineEndMergesNextLine()
    {
        var model = new EditorModel(["first", "second"]);
        model.MoveToLineEnd(); // cursor at end of "first"
        model.DeleteForward();
        Assert.Single(model.Lines);
        Assert.Equal("firstsecond", model.Lines[0]);
    }

    // ── BreakLine ─────────────────────────────────────────────────────────────

    [Fact]
    public void BreakLine_SplitsCurrentLineAtCursor()
    {
        var model = new EditorModel(["Hello World"]);
        model.MoveRight(); model.MoveRight(); model.MoveRight(); model.MoveRight(); model.MoveRight();
        // cursor at col 5
        model.BreakLine();
        Assert.Equal(2, model.Lines.Count);
        Assert.Equal("Hello", model.Lines[0]);
        Assert.Equal(" World", model.Lines[1]);
        Assert.Equal(1, model.CursorRow);
        Assert.Equal(0, model.CursorCol);
    }

    // ── cursor wrap ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveLeft_AtLineStartWrapsToEndOfPreviousLine()
    {
        var model = new EditorModel(["abc", "def"]);
        model.MoveDown(); // row 1, col 0
        model.MoveLeft();
        Assert.Equal(0, model.CursorRow);
        Assert.Equal(3, model.CursorCol); // end of "abc"
    }

    [Fact]
    public void MoveRight_AtLineEndWrapsToStartOfNextLine()
    {
        var model = new EditorModel(["abc", "def"]);
        model.MoveToLineEnd(); // col 3
        model.MoveRight();
        Assert.Equal(1, model.CursorRow);
        Assert.Equal(0, model.CursorCol);
    }

    // ── MarkClean ─────────────────────────────────────────────────────────────

    [Fact]
    public void MarkClean_ClearsDirtyFlag()
    {
        var model = new EditorModel(["text"]);
        model.InsertChar('X');
        Assert.True(model.IsDirty);
        model.MarkClean();
        Assert.False(model.IsDirty);
    }
}
