using System.Diagnostics.CodeAnalysis;
using System.Text;
using CSharpFar.Console.Ansi;
using CSharpFar.Console.Input;

namespace CSharpFar.Tests;

public sealed class AnsiInputParserTests
{
    [Theory]
    [InlineData("\u001b[A", ConsoleKey.UpArrow)]
    [InlineData("\u001b[B", ConsoleKey.DownArrow)]
    [InlineData("\u001b[C", ConsoleKey.RightArrow)]
    [InlineData("\u001b[D", ConsoleKey.LeftArrow)]
    [InlineData("\u001bOA", ConsoleKey.UpArrow)]
    [InlineData("\u001bOB", ConsoleKey.DownArrow)]
    [InlineData("\u001bOC", ConsoleKey.RightArrow)]
    [InlineData("\u001bOD", ConsoleKey.LeftArrow)]
    [InlineData("\u001b[H", ConsoleKey.Home)]
    [InlineData("\u001b[1~", ConsoleKey.Home)]
    [InlineData("\u001b[F", ConsoleKey.End)]
    [InlineData("\u001b[4~", ConsoleKey.End)]
    [InlineData("\u001b[3~", ConsoleKey.Delete)]
    [InlineData("\u001bOP", ConsoleKey.F1)]
    [InlineData("\u001bOQ", ConsoleKey.F2)]
    [InlineData("\u001bOR", ConsoleKey.F3)]
    [InlineData("\u001bOS", ConsoleKey.F4)]
    [InlineData("\u001b[11~", ConsoleKey.F1)]
    [InlineData("\u001b[12~", ConsoleKey.F2)]
    [InlineData("\u001b[13~", ConsoleKey.F3)]
    [InlineData("\u001b[14~", ConsoleKey.F4)]
    [InlineData("\u001b[[A", ConsoleKey.F1)]
    [InlineData("\u001b[[B", ConsoleKey.F2)]
    [InlineData("\u001b[[C", ConsoleKey.F3)]
    [InlineData("\u001b[[D", ConsoleKey.F4)]
    [InlineData("\u001b[[E", ConsoleKey.F5)]
    [InlineData("\u001b[15~", ConsoleKey.F5)]
    [InlineData("\u001b[17~", ConsoleKey.F6)]
    [InlineData("\u001b[18~", ConsoleKey.F7)]
    [InlineData("\u001b[19~", ConsoleKey.F8)]
    [InlineData("\u001b[20~", ConsoleKey.F9)]
    [InlineData("\u001b[21~", ConsoleKey.F10)]
    [InlineData("\u001b[23~", ConsoleKey.F11)]
    [InlineData("\u001b[24~", ConsoleKey.F12)]
    public void ParseSingle_MapsEscapeSequences(string sequence, ConsoleKey expected)
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes(sequence));

        Assert.Equal(expected, key.Key);
    }

    [Theory]
    [InlineData("\u001b[1;5C", ConsoleKey.RightArrow, ConsoleModifiers.Control)]
    [InlineData("\u001b[1;2D", ConsoleKey.LeftArrow, ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;6A", ConsoleKey.UpArrow, ConsoleModifiers.Control | ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;3B", ConsoleKey.DownArrow, ConsoleModifiers.Alt)]
    [InlineData("\u001b[15;2~", ConsoleKey.F5, ConsoleModifiers.Shift)]
    [InlineData("\u001b[1;5P", ConsoleKey.F1, ConsoleModifiers.Control)]
    [InlineData("\u001b[Z", ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("\u001b1", ConsoleKey.D1, ConsoleModifiers.Alt)]
    [InlineData("\u001bo", ConsoleKey.O, ConsoleModifiers.Alt)]
    [InlineData("\u001b\u001b[D", ConsoleKey.LeftArrow, ConsoleModifiers.Alt)]
    [InlineData("\u000f", ConsoleKey.O, ConsoleModifiers.Control)]
    public void ParseSingle_MapsModifiers(string sequence, ConsoleKey expectedKey, ConsoleModifiers expectedModifiers)
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes(sequence));

        Assert.Equal(expectedKey, key.Key);
        Assert.Equal(expectedModifiers, key.Modifiers);
    }

    [Fact]
    public void ParseSingle_MapsUtf8Character()
    {
        var key = AnsiInputParser.ParseSingle(Encoding.UTF8.GetBytes("Ж"));

        Assert.Equal('Ж', key.KeyChar);
    }

    [Theory]
    [InlineData("A", ConsoleKey.A, ConsoleModifiers.None)]
    [InlineData("\r", ConsoleKey.Enter, ConsoleModifiers.None)]
    [InlineData("\t", ConsoleKey.Tab, ConsoleModifiers.None)]
    [InlineData("\u007f", ConsoleKey.Backspace, ConsoleModifiers.None)]
    [InlineData("\u001b[2~", ConsoleKey.Insert, ConsoleModifiers.None)]
    [InlineData("\u001b[5~", ConsoleKey.PageUp, ConsoleModifiers.None)]
    [InlineData("\u001b[6~", ConsoleKey.PageDown, ConsoleModifiers.None)]
    [InlineData("\u001b[15;3~", ConsoleKey.F5, ConsoleModifiers.Alt)]
    [InlineData("\u001b[15;5~", ConsoleKey.F5, ConsoleModifiers.Control)]
    [InlineData("\u001bA", ConsoleKey.A, ConsoleModifiers.Alt)]
    [InlineData("\u001b\u001b[D", ConsoleKey.LeftArrow, ConsoleModifiers.Alt)]
    public void ProductionParser_MapsKeyboardEvents(
        string sequence,
        ConsoleKey expectedKey,
        ConsoleModifiers expectedModifiers)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(sequence));
        var parser = new AnsiConsoleInputParser();

        bool parsed = parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent);

        var keyEvent = Assert.IsType<KeyConsoleInputEvent>(inputEvent);
        Assert.True(parsed);
        Assert.Equal(expectedKey, keyEvent.Key.Key);
        Assert.Equal(expectedModifiers, keyEvent.Key.Modifiers);
    }

    [Fact]
    public void ProductionParser_MapsAltUtf8Character()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("\u001bф"));
        var parser = new AnsiConsoleInputParser();

        Assert.True(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));

        var keyEvent = Assert.IsType<KeyConsoleInputEvent>(inputEvent);
        Assert.Equal('ф', keyEvent.Key.KeyChar);
        Assert.True(keyEvent.Key.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    [Fact]
    public void ProductionParser_ReturnsSgrMouseEvent()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("\u001b[<0;42;10M"));
        var parser = new AnsiConsoleInputParser();

        Assert.True(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));

        var mouse = Assert.IsType<MouseConsoleInputEvent>(inputEvent);
        Assert.Equal((41, 9, MouseButton.Left, MouseEventKind.Down),
            (mouse.X, mouse.Y, mouse.Button, mouse.Kind));
    }

    [Fact]
    public void ProductionParser_SynthesizesDoubleClickForSameCellWithinInterval()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(
            "\u001b[<0;42;10M\u001b[<0;42;10m\u001b[<0;42;10M"));
        long timestamp = 1_000;
        var parser = new AnsiConsoleInputParser(50, () => timestamp);
        var reader = new StreamAnsiInputByteReader(input, null);

        Assert.True(parser.TryRead(reader, out var firstPress));
        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(firstPress).Kind);
        Assert.True(parser.TryRead(reader, out var release));
        Assert.Equal(MouseEventKind.Up, Assert.IsType<MouseConsoleInputEvent>(release).Kind);

        timestamp += 300;
        Assert.True(parser.TryRead(reader, out var secondPress));

        var doubleClick = Assert.IsType<MouseConsoleInputEvent>(secondPress);
        Assert.Equal(MouseEventKind.DoubleClick, doubleClick.Kind);
        Assert.Equal((41, 9, MouseButton.Left),
            (doubleClick.X, doubleClick.Y, doubleClick.Button));
    }

    [Fact]
    public void ProductionParser_ResetMouseState_PreventsDoubleClick()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(
            "\u001b[<0;42;10M\u001b[<0;42;10M"));
        long timestamp = 1_000;
        var parser = new AnsiConsoleInputParser(50, () => timestamp);
        var reader = new StreamAnsiInputByteReader(input, null);

        Assert.True(parser.TryRead(reader, out var firstPress));
        parser.ResetMouseState();
        timestamp += 100;
        Assert.True(parser.TryRead(reader, out var secondPress));

        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(firstPress).Kind);
        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(secondPress).Kind);
    }

    [Theory]
    [InlineData("\u001b[<0;43;10M", 300)]
    [InlineData("\u001b[<0;42;10M", 501)]
    public void ProductionParser_DoesNotSynthesizeDoubleClickForDifferentCellOrExpiredInterval(
        string secondPress,
        int elapsedMilliseconds)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(
            "\u001b[<0;42;10M\u001b[<0;42;10m" + secondPress));
        long timestamp = 1_000;
        var parser = new AnsiConsoleInputParser(50, () => timestamp);
        var reader = new StreamAnsiInputByteReader(input, null);

        Assert.True(parser.TryRead(reader, out _));
        Assert.True(parser.TryRead(reader, out _));
        timestamp += elapsedMilliseconds;
        Assert.True(parser.TryRead(reader, out var inputEvent));

        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(inputEvent).Kind);
    }

    [Fact]
    public void ProductionParser_IgnoresMalformedSgrMouseEvent()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("\u001b[<x;42;10M"));
        var parser = new AnsiConsoleInputParser();

        Assert.False(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));
        Assert.Null(inputEvent);
    }

    [Fact]
    public void ProductionParser_IgnoresButtonlessMotion()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("\u001b[<35;42;10M"));
        var parser = new AnsiConsoleInputParser();

        Assert.False(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));
        Assert.Null(inputEvent);
    }

    [Fact]
    public void ProductionParser_MapsUnknownKeyboardSequenceToEscape()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes("\u001b[999~"));
        var parser = new AnsiConsoleInputParser();

        Assert.True(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));

        var keyEvent = Assert.IsType<KeyConsoleInputEvent>(inputEvent);
        Assert.Equal(ConsoleKey.Escape, keyEvent.Key.Key);
    }

    [Fact]
    public void UnixRawTerminalInputReader_ManagesMouseAndRawModeLifetime()
    {
        var terminalMode = new FakeTerminalInputMode();
        var controls = new List<string>();
        var reader = new UnixRawTerminalInputReader(
            new StreamAnsiInputByteReader(new MemoryStream(), null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            controls.Add,
            terminalMode);

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.EnableCount);
        Assert.Equal("\u001b[?1002h\u001b[?1006h", controls[^1]);

        reader.SuspendInputMode();
        reader.SuspendInputMode();

        Assert.False(reader.MouseTrackingEnabled);
        Assert.Equal(1, terminalMode.RestoreCount);
        Assert.Equal("\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l", controls[^1]);

        reader.RestoreInputMode();
        reader.RestoreInputMode();

        Assert.True(reader.MouseTrackingEnabled);
        Assert.Equal(2, terminalMode.EnableCount);
        Assert.Equal("\u001b[?1002h\u001b[?1006h", controls[^1]);

        reader.Dispose();
        reader.Dispose();

        Assert.True(terminalMode.Disposed);
        Assert.Equal(1, terminalMode.DisposeCount);
        Assert.Equal("\u001b[?1000l\u001b[?1002l\u001b[?1003l\u001b[?1006l", controls[^1]);
    }

    [Fact]
    public void UnixRawTerminalInputReader_SuspendRestore_ResetsMouseSemanticState()
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(
            "\u001b[<0;42;10M\u001b[<0;42;10M"));
        long timestamp = 1_000;
        var reader = new UnixRawTerminalInputReader(
            new StreamAnsiInputByteReader(input, null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            _ => { },
            new FakeTerminalInputMode(),
            parser: new AnsiConsoleInputParser(50, () => timestamp));

        Assert.True(reader.TryReadInput(intercept: true, out var firstPress));
        reader.SuspendInputMode();
        timestamp += 100;
        reader.RestoreInputMode();
        Assert.True(reader.TryReadInput(intercept: true, out var secondPress));

        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(firstPress).Kind);
        Assert.Equal(MouseEventKind.Down, Assert.IsType<MouseConsoleInputEvent>(secondPress).Kind);

        reader.Dispose();
    }

    [Fact]
    public void UnixRawTerminalInputReader_RestoresModeWhenMouseEnableFails()
    {
        var terminalMode = new FakeTerminalInputMode();

        Assert.Throws<IOException>(() => new UnixRawTerminalInputReader(
            new StreamAnsiInputByteReader(new MemoryStream(), null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            _ => throw new IOException("write failed"),
            terminalMode));

        Assert.Equal(1, terminalMode.RestoreCount);
        Assert.True(terminalMode.Disposed);
    }

    [Fact]
    public void ShiftDown_EmitsShiftModifierEvent()
    {
        var last = default(ConsoleModifiers);

        Assert.True(LinuxEvdevModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref last,
            leftShiftDown: true,
            rightShiftDown: false,
            out var inputEvent));

        Assert.Equal(ConsoleModifiers.Shift, inputEvent.Modifiers);
    }

    [Fact]
    public void ShiftUp_EmitsPlainModifierEvent()
    {
        var last = ConsoleModifiers.Shift;

        Assert.True(LinuxEvdevModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref last,
            leftShiftDown: false,
            rightShiftDown: false,
            out var inputEvent));

        Assert.Equal(default, inputEvent.Modifiers);
    }

    [Fact]
    public void DuplicateShiftDown_DoesNotEmitDuplicateEvent()
    {
        var last = ConsoleModifiers.Shift;

        Assert.False(LinuxEvdevModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref last,
            leftShiftDown: true,
            rightShiftDown: false,
            out var inputEvent));

        Assert.Null(inputEvent);
    }

    [Fact]
    public void LeftAndRightShift_ReleaseOneStillKeepsShiftActive()
    {
        var last = ConsoleModifiers.Shift;

        Assert.False(LinuxEvdevModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref last,
            leftShiftDown: false,
            rightShiftDown: true,
            out var inputEvent));

        Assert.Null(inputEvent);
    }

    [Fact]
    public void ReleaseLastShift_EmitsPlainModifierEvent()
    {
        var last = ConsoleModifiers.Shift;

        Assert.True(LinuxEvdevModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref last,
            leftShiftDown: false,
            rightShiftDown: false,
            out var inputEvent));

        Assert.Equal(default, inputEvent.Modifiers);
    }

    [Fact]
    public void UnixRawTerminalInputReader_ReturnsModifierEventBeforeBlockingInput()
    {
        using var tracker = new FakeModifierKeyTracker();
        tracker.Pending = new ModifierKeyConsoleInputEvent(ConsoleModifiers.Shift);
        using var reader = CreateReaderWithTracker(tracker);

        var inputEvent = reader.ReadInput(intercept: true, new CancellationTokenSource(1000).Token);

        Assert.Same(tracker.LastReturned, inputEvent);
    }

    [Fact]
    public void UnixRawTerminalInputReader_TryReadInput_ReturnsPendingModifierEvent()
    {
        using var tracker = new FakeModifierKeyTracker();
        tracker.Pending = new ModifierKeyConsoleInputEvent(ConsoleModifiers.Shift);
        using var reader = CreateReaderWithTracker(tracker);

        Assert.True(reader.TryReadInput(intercept: true, out var inputEvent));

        Assert.Same(tracker.LastReturned, inputEvent);
    }

    [Fact]
    public void UnixRawTerminalInputReader_SuspendInputMode_SuspendsModifierTracker()
    {
        using var tracker = new FakeModifierKeyTracker();
        using var reader = CreateReaderWithTracker(tracker);

        reader.SuspendInputMode();

        Assert.Equal(1, tracker.SuspendCount);
    }

    [Fact]
    public void UnixRawTerminalInputReader_Dispose_DisposesModifierTracker()
    {
        var tracker = new FakeModifierKeyTracker();
        using (CreateReaderWithTracker(tracker))
        {
        }

        Assert.True(tracker.Disposed);
    }

    private sealed class FakeTerminalInputMode : ITerminalInputMode
    {
        public int EnableCount { get; private set; }
        public int RestoreCount { get; private set; }
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void EnableRawMode() => EnableCount++;

        public void RestoreOriginalMode() => RestoreCount++;

        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }

    private static UnixRawTerminalInputReader CreateReaderWithTracker(FakeModifierKeyTracker tracker) =>
        new(
            new StreamAnsiInputByteReader(new MemoryStream(), null),
            () => new CSharpFar.Console.Models.ConsoleSize(80, 25),
            () => { },
            _ => { },
            new FakeTerminalInputMode(),
            tracker);

    private sealed class FakeModifierKeyTracker : IModifierKeyTracker
    {
        public ModifierKeyConsoleInputEvent? Pending { get; set; }
        public ModifierKeyConsoleInputEvent? LastReturned { get; private set; }
        public int SuspendCount { get; private set; }
        public bool Disposed { get; private set; }

        public string BackendName => "fake";

        public ModifierKeyTrackingSnapshot GetSnapshot() =>
            new(
                BackendName,
                IsPlatformSupported: true,
                IsEnabled: true,
                CanTrackShiftOnly: true,
                Status: ModifierKeyTrackingStatus.Enabled,
                FailureReason: null,
                Devices: []);

        public bool TryCreateInputEvent([NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
        {
            inputEvent = Pending;
            LastReturned = Pending;
            Pending = null;
            return inputEvent is not null;
        }

        public void ObserveConsoleInput(ConsoleInputEvent inputEvent)
        {
        }

        public void Suspend() => SuspendCount++;

        public void Resume()
        {
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void ParseSingle_MapsCtrlLetter()
    {
        var key = AnsiInputParser.ParseSingle([0x03]);

        Assert.Equal(ConsoleKey.C, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void ParseSingle_MapsStandaloneEscape()
    {
        var key = AnsiInputParser.ParseSingle([0x1b]);

        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.Equal('\x1b', key.KeyChar);
    }

    [Fact]
    public void Read_ReturnsRawBytes()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("\u001b[A"));

        var result = new AnsiInputParser().Read(input);

        Assert.Equal(ConsoleKey.UpArrow, result.Key.Key);
        Assert.Equal([0x1b, 0x5b, 0x41], result.Bytes);
    }

    [Theory]
    [InlineData("\u001b[<0;10;5M", MouseButton.Left, MouseEventKind.Down, 9, 4)]
    [InlineData("\u001b[<0;10;5m", MouseButton.Left, MouseEventKind.Up, 9, 4)]
    [InlineData("\u001b[<1;10;5M", MouseButton.Middle, MouseEventKind.Down, 9, 4)]
    [InlineData("\u001b[<1;10;5m", MouseButton.Middle, MouseEventKind.Up, 9, 4)]
    [InlineData("\u001b[<2;10;5M", MouseButton.Right, MouseEventKind.Down, 9, 4)]
    [InlineData("\u001b[<2;10;5m", MouseButton.Right, MouseEventKind.Up, 9, 4)]
    [InlineData("\u001b[<32;10;5M", MouseButton.Left, MouseEventKind.Move, 9, 4)]
    [InlineData("\u001b[<64;10;5M", MouseButton.WheelUp, MouseEventKind.Wheel, 9, 4)]
    [InlineData("\u001b[<65;10;5M", MouseButton.WheelDown, MouseEventKind.Wheel, 9, 4)]
    public void SgrMouseInputParser_ParsesMouseEvents(
        string sequence,
        MouseButton expectedButton,
        MouseEventKind expectedKind,
        int expectedX,
        int expectedY)
    {
        var lastPressedButton = MouseButton.Left;

        bool parsed = SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes(sequence),
            ref lastPressedButton,
            out var result,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(result);
        Assert.Equal(expectedButton, result.Mouse.Button);
        Assert.Equal(expectedKind, result.Mouse.Kind);
        Assert.Equal(expectedX, result.Mouse.X);
        Assert.Equal(expectedY, result.Mouse.Y);
    }

    [Theory]
    [InlineData("\u001b[<4;10;5M", MouseKeyModifiers.Shift)]
    [InlineData("\u001b[<8;10;5M", MouseKeyModifiers.Alt)]
    [InlineData("\u001b[<16;10;5M", MouseKeyModifiers.Control)]
    [InlineData("\u001b[<28;10;5M", MouseKeyModifiers.Shift | MouseKeyModifiers.Alt | MouseKeyModifiers.Control)]
    public void SgrMouseInputParser_ParsesModifiers(string sequence, MouseKeyModifiers expectedModifiers)
    {
        var lastPressedButton = MouseButton.Left;

        bool parsed = SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes(sequence),
            ref lastPressedButton,
            out var result,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(result);
        Assert.Equal(expectedModifiers, result.Mouse.Modifiers);
    }

    [Theory]
    [InlineData("\u001b[<2;10;5m", MouseButton.Right)]
    [InlineData("\u001b[<1;10;5m", MouseButton.Middle)]
    public void ProductionParser_ReleaseAfterReset_UsesEncodedButton(string sequence, MouseButton expectedButton)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(sequence));
        var parser = new AnsiConsoleInputParser();

        parser.ResetMouseState();

        Assert.True(parser.TryRead(new StreamAnsiInputByteReader(input, null), out var inputEvent));
        var mouse = Assert.IsType<MouseConsoleInputEvent>(inputEvent);
        Assert.Equal(expectedButton, mouse.Button);
        Assert.Equal(MouseEventKind.Up, mouse.Kind);
    }

    [Fact]
    public void SgrMouseInputParser_UsesEncodedButtonForRelease()
    {
        var lastPressedButton = MouseButton.Left;
        Assert.True(SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes("\u001b[<2;10;5M"),
            ref lastPressedButton,
            out _,
            out _));

        Assert.True(SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes("\u001b[<0;10;5m"),
            ref lastPressedButton,
            out var release,
            out var error), error);

        Assert.NotNull(release);
        Assert.Equal(MouseButton.Left, release.Mouse.Button);
        Assert.Equal(MouseEventKind.Up, release.Mouse.Kind);
        Assert.Equal(0, release.EncodedButton);
        Assert.Equal('m', release.Final);
    }

    [Fact]
    public void SgrMouseInputParser_UsesLastPressedButtonOnlyForCode3Release()
    {
        var lastPressedButton = MouseButton.Right;

        Assert.True(SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes("\u001b[<3;10;5m"),
            ref lastPressedButton,
            out var release,
            out var error), error);

        Assert.NotNull(release);
        Assert.Equal(MouseButton.Right, release.Mouse.Button);
        Assert.Equal(MouseEventKind.Up, release.Mouse.Kind);
    }

    [Fact]
    public void SgrMouseInputParser_ReleaseDoesNotDependOnLastDown()
    {
        var lastPressedButton = MouseButton.Left;
        var events = new List<MouseConsoleInputEvent>();

        foreach (string sequence in new[]
        {
            "\u001b[<0;10;5M",
            "\u001b[<2;10;5M",
            "\u001b[<0;10;5m",
            "\u001b[<2;10;5m",
        })
        {
            Assert.True(SgrMouseInputParser.TryParse(
                Encoding.ASCII.GetBytes(sequence),
                ref lastPressedButton,
                out var result,
                out var error), error);
            events.Add(result.Mouse);
        }

        Assert.Equal(
            [
                (MouseButton.Left, MouseEventKind.Down),
                (MouseButton.Right, MouseEventKind.Down),
                (MouseButton.Left, MouseEventKind.Up),
                (MouseButton.Right, MouseEventKind.Up),
            ],
            events.Select(e => (e.Button, e.Kind)));
    }

    [Theory]
    [InlineData("\u001b[A", null)]
    [InlineData("abc", null)]
    [InlineData("\u001b[<0;10M", "SGR mouse sequence must contain Cb, Px, and Py.")]
    [InlineData("\u001b[<x;10;5M", "SGR mouse sequence contains a non-numeric or overflowing field.")]
    public void SgrMouseInputParser_RejectsNonMouseSequences(string sequence, string? expectedError)
    {
        var lastPressedButton = MouseButton.Left;

        bool parsed = SgrMouseInputParser.TryParse(
            Encoding.ASCII.GetBytes(sequence),
            ref lastPressedButton,
            out var result,
            out var error);

        Assert.False(parsed);
        Assert.Null(result);
        Assert.Equal(expectedError, error);
    }
}
