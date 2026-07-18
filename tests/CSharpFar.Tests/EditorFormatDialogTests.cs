using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class EditorFormatDialogTests
{
    [Fact]
    public void Show_EnterOnEncoding_ReturnsCurrentFormat()
    {
        var driver = Driver(Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.Equal(Encoding.UTF8.CodePage, result.Encoding.CodePage);
        Assert.True(result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.CrLf, result.LineEnding);
    }

    [Fact]
    public void Show_EnterOnBom_ReturnsCurrentFormatWithoutTogglingBom()
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.Lf));

        Assert.NotNull(result);
        Assert.True(result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.Lf, result.LineEnding);
    }

    [Fact]
    public void Show_RightThenEnter_AppliesOnlyRightChange()
    {
        var driver = Driver(Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.Equal(Encoding.Unicode.CodePage, result.Encoding.CodePage);
        Assert.True(result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.CrLf, result.LineEnding);
    }

    [Fact]
    public void Show_SpaceChangesValueButDoesNotClose()
    {
        var driver = Driver(Key(ConsoleKey.Spacebar), Key(ConsoleKey.F10));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.Null(result);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void Show_CancelKeysReturnNull(ConsoleKey key)
    {
        var driver = Driver(Key(key));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.Null(result);
    }

    [Fact]
    public void Show_HidesCursorOnInitialAndChangedFocusFrames()
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Escape));
        var cursorStates = new List<bool>();
        driver.BeforeReadInput = d => cursorStates.Add(d.CursorVisible);

        _ = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.Equal([false], cursorStates);
        Assert.False(driver.CursorVisible);
    }

    private static EditorDocumentFormat Current(bool emitBom, EditorLineEnding lineEnding) =>
        new(Encoding.UTF8, emitBom, lineEnding, "UTF-8");

    private static FakeConsoleDriver Driver(params ConsoleInputEvent[] inputs)
    {
        var driver = new FakeConsoleDriver(width: 80, height: 25);
        foreach (ConsoleInputEvent input in inputs)
            driver.EnqueueInput(input);
        return driver;
    }

    private static KeyConsoleInputEvent Key(ConsoleKey key) =>
        new(new ConsoleKeyInfo('\0', key, shift: false, alt: false, control: false));
}
