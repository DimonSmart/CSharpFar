using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public class CommandLineStateTests
{
    [Fact]
    public void Insert_AddsCharacterAtCursor()
    {
        var s = new CommandLineState();
        s.Insert('a');
        s.Insert('b');
        s.Insert('c');

        Assert.Equal("abc", s.Text);
        Assert.Equal(3, s.CursorPosition);
    }

    [Fact]
    public void Insert_InsertsAtMiddleOfText()
    {
        var s = new CommandLineState();
        s.Insert('a');
        s.Insert('c');
        s.MoveCursor(-1); // cursor between a and c
        s.Insert('b');

        Assert.Equal("abc", s.Text);
        Assert.Equal(2, s.CursorPosition);
    }

    [Fact]
    public void InsertText_InsertsAtCursor()
    {
        var s = new CommandLineState();
        s.SetText("ac");
        s.MoveCursor(-1);

        s.InsertText("b1");

        Assert.Equal("ab1c", s.Text);
        Assert.Equal(3, s.CursorPosition);
    }

    [Fact]
    public void DeleteBack_RemovesCharBeforeCursor()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.DeleteBack();

        Assert.Equal("hell", s.Text);
        Assert.Equal(4, s.CursorPosition);
    }

    [Fact]
    public void DeleteBack_DoesNothingAtStart()
    {
        var s = new CommandLineState();
        s.SetText("hi");
        s.MoveToStart();
        s.DeleteBack();

        Assert.Equal("hi", s.Text);
        Assert.Equal(0, s.CursorPosition);
    }

    [Fact]
    public void DeleteForward_RemovesCharAtCursor()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.MoveToStart();
        s.DeleteForward();

        Assert.Equal("ello", s.Text);
        Assert.Equal(0, s.CursorPosition);
    }

    [Fact]
    public void DeleteForward_DoesNothingAtEnd()
    {
        var s = new CommandLineState();
        s.SetText("hi");
        s.DeleteForward();

        Assert.Equal("hi", s.Text);
    }

    [Fact]
    public void MoveCursor_ClampsToValidRange()
    {
        var s = new CommandLineState();
        s.SetText("hello");

        s.MoveCursor(-100);
        Assert.Equal(0, s.CursorPosition);

        s.MoveCursor(+100);
        Assert.Equal(5, s.CursorPosition);
    }

    [Fact]
    public void MoveToStart_SetsCursorToZero()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.MoveToStart();
        Assert.Equal(0, s.CursorPosition);
    }

    [Fact]
    public void MoveToEnd_SetsCursorToLength()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.MoveToStart();
        s.MoveToEnd();
        Assert.Equal(5, s.CursorPosition);
    }

    [Fact]
    public void Clear_EmptiesBufferAndResetsCursor()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.Clear();

        Assert.Equal(string.Empty, s.Text);
        Assert.Equal(0, s.CursorPosition);
        Assert.False(s.HasText);
    }

    [Fact]
    public void SetText_ReplacesContentAndMovestoEnd()
    {
        var s = new CommandLineState();
        s.Insert('x');
        s.SetText("dir /s");

        Assert.Equal("dir /s", s.Text);
        Assert.Equal(6, s.CursorPosition);
    }

    [Fact]
    public void HasText_FalseWhenEmpty()
    {
        var s = new CommandLineState();
        Assert.False(s.HasText);
    }

    [Fact]
    public void HasText_TrueAfterInsert()
    {
        var s = new CommandLineState();
        s.Insert('a');
        Assert.True(s.HasText);
    }

    [Fact]
    public void SelectAll_SelectsWholeBufferAndMovesCursorToEnd()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.MoveToStart();

        s.SelectAll();

        Assert.True(s.HasSelection);
        Assert.Equal(0, s.SelectionStart);
        Assert.Equal(5, s.SelectionLength);
        Assert.Equal(5, s.CursorPosition);
    }

    [Fact]
    public void Insert_ReplacesSelection()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.SelectAll();

        s.Insert('x');

        Assert.Equal("x", s.Text);
        Assert.Equal(1, s.CursorPosition);
        Assert.False(s.HasSelection);
    }

    [Fact]
    public void DeleteBack_RemovesSelection()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.SelectAll();

        s.DeleteBack();

        Assert.Equal(string.Empty, s.Text);
        Assert.Equal(0, s.CursorPosition);
        Assert.False(s.HasSelection);
    }

    [Fact]
    public void MoveCursor_ClearsSelection()
    {
        var s = new CommandLineState();
        s.SetText("hello");
        s.SelectAll();

        s.MoveCursor(-1);

        Assert.False(s.HasSelection);
        Assert.Equal(4, s.CursorPosition);
    }
}
