using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Show_LeftOnBom_TogglesBomAndEnterApplies(bool initialBom)
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.LeftArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(initialBom, EditorLineEnding.Lf));

        Assert.NotNull(result);
        Assert.Equal(!initialBom, result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.Lf, result.LineEnding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Show_RightOnBom_TogglesBomAndEnterApplies(bool initialBom)
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(initialBom, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.Equal(!initialBom, result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.CrLf, result.LineEnding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Show_SpaceOnBom_TogglesBomAndEnterApplies(bool initialBom)
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Spacebar), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(initialBom, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.Equal(!initialBom, result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.CrLf, result.LineEnding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Show_EnterOnBom_DoesNotToggleBom(bool initialBom)
    {
        var driver = Driver(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(initialBom, EditorLineEnding.Lf));

        Assert.NotNull(result);
        Assert.Equal(initialBom, result.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.Lf, result.LineEnding);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Show_MouseClickOnBom_TogglesBom(bool initialBom)
    {
        var driver = Driver();
        EnqueueMouseClickOnRenderedText(driver, "BOM:", Key(ConsoleKey.Enter));

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(initialBom, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.Equal(!initialBom, result.EmitByteOrderMark);
    }

    [Fact]
    public void Show_EncodingAndLineEndingStillChangeWithLeftAndRight()
    {
        var encodingDriver = Driver(Key(ConsoleKey.RightArrow), Key(ConsoleKey.Enter));

        EditorDocumentFormat? encodingResult = new EditorFormatDialog(ModalTestHost.Create(encodingDriver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.NotNull(encodingResult);
        Assert.Equal(Encoding.Unicode.CodePage, encodingResult.Encoding.CodePage);
        Assert.True(encodingResult.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.CrLf, encodingResult.LineEnding);

        var lineEndingDriver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            Key(ConsoleKey.Enter));

        EditorDocumentFormat? lineEndingResult = new EditorFormatDialog(ModalTestHost.Create(lineEndingDriver))
            .Show(Current(emitBom: true, EditorLineEnding.CrLf));

        Assert.NotNull(lineEndingResult);
        Assert.Equal(Encoding.UTF8.CodePage, lineEndingResult.Encoding.CodePage);
        Assert.True(lineEndingResult.EmitByteOrderMark);
        Assert.Equal(EditorLineEnding.Lf, lineEndingResult.LineEnding);
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

    [Fact]
    public void Show_ResizeAfterChangingBom_DoesNotLoseChangedValue()
    {
        var driver = Driver(
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.RightArrow),
            new ConsoleResizeInputEvent(),
            Key(ConsoleKey.Enter));
        ResizeBeforeRead(driver, readNumber: 3, width: 100, height: 30);

        EditorDocumentFormat? result = new EditorFormatDialog(ModalTestHost.Create(driver))
            .Show(Current(emitBom: false, EditorLineEnding.CrLf));

        Assert.NotNull(result);
        Assert.True(result.EmitByteOrderMark);
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

    private static MouseConsoleInputEvent Mouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static void EnqueueMouseClickOnRenderedText(
        FakeConsoleDriver driver,
        string text,
        params ConsoleInputEvent[] afterClick)
    {
        driver.BeforeReadInput = d =>
        {
            FakeConsoleDriver.WriteRecord record = d.WriteRecords.First(r => r.Text.Contains(text, StringComparison.Ordinal));
            d.EnqueueInput(Mouse(record.X, record.Y));
            foreach (ConsoleInputEvent input in afterClick)
                d.EnqueueInput(input);
        };
    }

    private static void ResizeBeforeRead(FakeConsoleDriver driver, int readNumber, int width, int height)
    {
        int reads = 0;
        driver.BeforeReadInput = OnBeforeRead;

        void OnBeforeRead(FakeConsoleDriver current)
        {
            reads++;
            if (reads == readNumber)
                current.SetSize(width, height);
            else
                current.BeforeReadInput = OnBeforeRead;
        }
    }
}
