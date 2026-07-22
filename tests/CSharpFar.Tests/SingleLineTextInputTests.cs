using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class SingleLineTextInputTests
{
    [Fact]
    public void HandleKey_ControlASelectsAllAndNextTypingReplacesSelection()
    {
        var buffer = new CommandLineState();
        buffer.SetText("sample");
        string? error = "old error";

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.True(buffer.HasSelection);
        Assert.Equal(0, buffer.SelectionStart);
        Assert.Equal(6, buffer.SelectionLength);
        Assert.Equal("old error", error);

        result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false),
            ref error);

        Assert.Equal(TextInputKeyResult.TextChanged, result);
        Assert.Equal("x", buffer.Text);
        Assert.False(buffer.HasSelection);
        Assert.Null(error);
    }

    [Fact]
    public void HandleKey_ControlAAlsoAcceptsControlCharacter()
    {
        var buffer = new CommandLineState();
        buffer.SetText("sample");
        string? error = null;

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: false),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.True(buffer.HasSelection);
    }

    [Fact]
    public void HandleKey_ControlArrowsMoveByWord()
    {
        var buffer = new CommandLineState();
        buffer.SetText("alpha beta");
        string? error = null;

        var leftResult = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: false, alt: false, control: true),
            ref error);
        var rightResult = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: true),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, leftResult);
        Assert.Equal(TextInputKeyResult.Handled, rightResult);
        Assert.Equal(10, buffer.CursorPosition);
    }

    [Fact]
    public void HandleKey_ControlShiftArrowSelectsWord()
    {
        var buffer = new CommandLineState();
        buffer.SetText("alpha beta");
        string? error = null;

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, shift: true, alt: false, control: true),
            ref error);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.Equal("beta", buffer.SelectedText);
    }

    [Fact]
    public void HandleKey_ControlCCopiesSelectionToTextClipboard()
    {
        var buffer = new CommandLineState();
        buffer.SetText("alpha");
        buffer.SelectAll();
        var clipboard = new FakeTextClipboard();
        string? error = null;

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true),
            ref error,
            clipboard);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.Equal("alpha", clipboard.Text);
    }

    [Fact]
    public void HandleKey_ControlVPastesFromTextClipboard()
    {
        var buffer = new CommandLineState();
        var clipboard = new FakeTextClipboard { Text = "alpha\nbeta" };
        string? error = "old";

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true),
            ref error,
            clipboard);

        Assert.Equal(TextInputKeyResult.TextChanged, result);
        Assert.Equal("alpha beta", buffer.Text);
        Assert.Null(error);
    }

    [Fact]
    public void Render_UsesSelectionStyleForVisibleSelectedText()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 2);
        var screen = new ScreenRenderer(driver);
        var buffer = new CommandLineState();
        buffer.SetText("abcdef");
        buffer.SelectAll();

        var normal = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        UiTestRender.Render(screen, canvas =>
            SingleLineTextInput.Render(canvas, 1, 0, 7, buffer, normal, selected));

        Assert.Equal('a', driver.GetCell(1, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(1, 0).Foreground);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(1, 0).Background);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(6, 0).Background);
        Assert.Equal(ConsoleColor.Black, driver.GetCell(7, 0).Background);
    }

    [Fact]
    public void Render_LongInputAtEnd_ShowsBlankCellAfterLastCharacter()
    {
        var driver = new FakeConsoleDriver(width: 12, height: 1);
        var screen = new ScreenRenderer(driver);
        var buffer = new CommandLineState();
        buffer.SetText("abcdefghij");

        var normal = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        UiTestRender.Render(screen, canvas =>
            SingleLineTextInput.Render(canvas, 1, 0, 10, buffer, normal, selected));

        Assert.Equal("bcdefghij ", driver.GetRegionText(new Rect(1, 0, 10, 1)));
        Assert.Equal(10, SingleLineTextInput.GetCursorX(1, 10, buffer));
        Assert.Equal('j', driver.GetCell(9, 0).Character);
        Assert.Equal(' ', driver.GetCell(10, 0).Character);
    }

    [Fact]
    public void History_AddKeepsUniqueRecencyOrder()
    {
        var history = new SingleLineTextHistoryState();

        history.Add("first");
        history.Add("second");
        history.Add("first");
        history.Add("   ");

        Assert.Equal(["first", "second"], history.Items);
    }

    [Fact]
    public void HandleKey_WithHistoryTypingPrefixOpensMatchingDropdown()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        history.Add("compare");
        history.Add("delete");
        string? error = null;

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);

        Assert.Equal(TextInputKeyResult.TextChanged, result);
        Assert.True(history.IsDropdownOpen);
        Assert.Equal(["compare", "copy"], history.Matches);
    }

    [Fact]
    public void HandleKey_WithHistoryEnterAcceptsSelectedSuggestion()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        history.Add("compare");
        string? error = null;

        SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);
        SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);

        Assert.Equal(TextInputKeyResult.TextChanged, result);
        Assert.Equal("copy", buffer.Text);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void HandleKey_WithHistoryEscapeClosesDropdownWithoutChangingText()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        string? error = null;

        SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('c', ConsoleKey.C, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);

        var result = SingleLineTextInput.HandleKey(
            buffer,
            new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, shift: false, alt: false, control: false),
            ref error,
            history,
            availableDropdownContentRows: 10);

        Assert.Equal(TextInputKeyResult.Handled, result);
        Assert.Equal("c", buffer.Text);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void Render_WithHistoryDrawsArrowAndDropdown()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 8);
        var screen = new ScreenRenderer(driver);
        var buffer = new CommandLineState();
        buffer.SetText("c");
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        history.Add("compare");
        Assert.True(history.OpenForPrefix("c", availableContentRows: 5));
        var normal = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        UiTestRender.Render(screen, canvas =>
            SingleLineTextInput.Render(canvas, 1, 1, 12, buffer, normal, selected, history));

        Assert.Equal(SingleLineTextInput.HistoryDropdownArrow, driver.GetCell(12, 1).Character);
        Assert.Equal('┌', driver.GetCell(1, 2).Character);
        Assert.Equal('┐', driver.GetCell(12, 2).Character);
        Assert.Contains("compare", driver.GetRow(3));
        Assert.Contains("copy", driver.GetRow(4));
    }

    [Fact]
    public void History_DoesNotOpenWhenDropdownCannotFitOneContentRow()
    {
        var history = new SingleLineTextHistoryState();
        history.Add("copy");

        bool opened = history.OpenForPrefix("c", availableContentRows: 0);

        Assert.False(opened);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void Render_WithHistoryDropdownUsesAtMostTenRows()
    {
        var driver = new FakeConsoleDriver(width: 30, height: 20);
        var screen = new ScreenRenderer(driver);
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < 12; i++)
            history.Add("item-" + i);
        Assert.True(history.OpenAll(availableContentRows: 20));
        var normal = new CellStyle(ConsoleColor.Gray, ConsoleColor.Black);
        var selected = new CellStyle(ConsoleColor.Yellow, ConsoleColor.Blue);

        UiTestRender.Render(screen, canvas =>
            SingleLineTextInput.Render(canvas, 1, 1, 12, buffer, normal, selected, history));

        Assert.Equal('└', driver.GetCell(1, 13).Character);
        Assert.Equal(' ', driver.GetCell(1, 14).Character);
    }

    [Fact]
    public void HandleHistoryDropdownMouse_ClickSuggestionAcceptsValue()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        history.Add("compare");
        Assert.True(history.OpenForPrefix("c", availableContentRows: 5));
        ScrollBarDragState? drag = null;

        bool handled = SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            LeftMouse(2, 2),
            fieldX: 1,
            fieldY: 0,
            fieldWidth: 12,
            screenHeight: 8,
            ref drag);

        Assert.True(handled);
        Assert.Equal("compare", buffer.Text);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void HandleHistoryDropdownMouse_ClickScrollbarMovesFirstVisibleIndex()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < 20; i++)
            history.Add("item-" + i);
        Assert.True(history.OpenAll(availableContentRows: 5));
        ScrollBarDragState? drag = null;

        bool handled = SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            LeftMouse(12, 6),
            fieldX: 1,
            fieldY: 0,
            fieldWidth: 12,
            screenHeight: 8,
            ref drag);

        Assert.True(handled);
        Assert.True(history.FirstVisibleIndex > 0);
        Assert.True(history.IsDropdownOpen);
    }

    [Fact]
    public void HandleHistoryDropdownMouse_ClickOutsideClosesDropdown()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        history.Add("copy");
        Assert.True(history.OpenForPrefix("c", availableContentRows: 5));
        ScrollBarDragState? drag = null;

        bool handled = SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            LeftMouse(18, 7),
            fieldX: 1,
            fieldY: 0,
            fieldWidth: 12,
            screenHeight: 8,
            ref drag);

        Assert.True(handled);
        Assert.False(history.IsDropdownOpen);
    }

    [Fact]
    public void HistoryDropdownFrame_MouseUsesNormalizedFirstVisibleIndex()
    {
        var buffer = new CommandLineState();
        var history = new SingleLineTextHistoryState();
        for (int i = 0; i < 12; i++)
            history.Add("item-" + i);
        Assert.True(history.OpenAll(availableContentRows: 10));
        history.SetFirstVisibleIndex(8, availableContentRows: 10);

        var frame = SingleLineTextInput.CalculateHistoryDropdownFrame(
            fieldX: 1,
            fieldY: 0,
            fieldWidth: 12,
            screenHeight: 4,
            history);

        Assert.NotNull(frame);
        Assert.Equal(1, frame.Value.VisibleRows);
        Assert.Equal(frame.Value.FirstVisibleIndex, frame.Value.SelectedIndex);
        ScrollBarDragState? drag = null;
        string expected = history.Matches[frame.Value.FirstVisibleIndex];

        bool handled = SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            LeftMouse(frame.Value.ContentBounds.X, frame.Value.ContentBounds.Y),
            frame.Value,
            ref drag);

        Assert.True(handled);
        Assert.Equal(expected, buffer.Text);
    }

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private sealed class FakeTextClipboard : ITextClipboard
    {
        public string Text { get; set; } = string.Empty;

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }

        public bool TryGetText(out string text)
        {
            text = Text;
            return true;
        }
    }
}
