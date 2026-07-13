using System.Text;
using CSharpFar.Console.Ansi;
using CSharpFar.Console.Input;
using CSharpFar.Console.Win32;

namespace CSharpFar.Tests;

public sealed class MouseBackendParityTests
{
    private const uint Left = 0x0001;
    private const uint Right = 0x0002;
    private const uint Middle = 0x0004;
    private const uint MouseMoved = 0x0001;
    private const uint MouseWheeled = 0x0004;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;

    [Fact]
    public void EquivalentPhysicalInput_ProducesEquivalentSemanticStreams()
    {
        foreach (var sequence in Sequences())
        {
            long timestamp = 1_000;
            var ansi = new AnsiConsoleInputParser(50, () => timestamp);
            var ansiEvents = ParseAnsi(ansi, sequence.AnsiRecords, sequence.TimeStepMilliseconds, ref timestamp);
            timestamp = 1_000;
            var win32 = new Win32MouseInputParser();
            var normalizer = new MouseInputNormalizer(() => timestamp);
            var win32Events = new List<MouseConsoleInputEvent>();

            foreach (var record in sequence.Win32Records)
            {
                var physical = win32.Parse(record, windowLeft: 11, windowTop: 26);
                if (physical is not null)
                    win32Events.Add(normalizer.Normalize(physical));

                timestamp += sequence.TimeStepMilliseconds;
            }

            Assert.Equal(ansiEvents.Select(Fields), win32Events.Select(Fields));
        }
    }

    private static IEnumerable<MouseParitySequence> Sequences()
    {
        yield return new(["\u001b[<0;10;5M", "\u001b[<0;10;5m"], [Record(Left), Record(0)]);
        yield return new(["\u001b[<2;10;5M", "\u001b[<2;10;5m"], [Record(Right), Record(0)]);
        yield return new(["\u001b[<1;10;5M", "\u001b[<1;10;5m"], [Record(Middle), Record(0)]);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m", "\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [Record(Left), Record(0), Record(Left), Record(0)]);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m", "\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [Record(Left), Record(0), Record(Left), Record(0)],
            TimeStepMilliseconds: 600);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<32;11;5M", "\u001b[<32;12;5M", "\u001b[<0;12;5m"],
            [Record(Left), Record(Left, MouseMoved, 21), Record(Left, MouseMoved, 22), Record(0, x: 22)]);
        yield return new(["\u001b[<64;10;5M", "\u001b[<65;10;5M"], [Record(120u << 16, MouseWheeled), Record(0xFF88u << 16, MouseWheeled)]);
        yield return new(["\u001b[<6;10;5M", "\u001b[<6;10;5m"], [Record(Right, controlKeyState: ShiftPressed), Record(0, controlKeyState: ShiftPressed)]);
        yield return new(["\u001b[<10;10;5M", "\u001b[<10;10;5m"], [Record(Right, controlKeyState: RightAltPressed), Record(0, controlKeyState: RightAltPressed)]);
        yield return new(["\u001b[<18;10;5M", "\u001b[<18;10;5m"], [Record(Right, controlKeyState: LeftCtrlPressed), Record(0, controlKeyState: LeftCtrlPressed)]);
        yield return new(["\u001b[<22;10;5M", "\u001b[<22;10;5m"], [Record(Right, controlKeyState: ShiftPressed | LeftCtrlPressed), Record(0, controlKeyState: ShiftPressed | LeftCtrlPressed)]);
    }

    private static MouseConsoleInputEvent[] ParseAnsi(
        AnsiConsoleInputParser parser,
        IEnumerable<string> records,
        int timeStepMilliseconds,
        ref long timestamp)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(string.Concat(records)));
        var reader = new StreamAnsiInputByteReader(input, null);
        var result = new List<MouseConsoleInputEvent>();
        foreach (string _ in records)
        {
            Assert.True(parser.TryRead(reader, out var inputEvent));
            result.Add(Assert.IsType<MouseConsoleInputEvent>(inputEvent));
            timestamp += timeStepMilliseconds;
        }

        return [.. result];
    }

    private static MouseEventRecord Record(
        uint buttonState,
        uint eventFlags = 0,
        short x = 20,
        short y = 30,
        uint controlKeyState = 0) =>
        new()
        {
            ButtonState = buttonState,
            EventFlags = eventFlags,
            MousePositionX = x,
            MousePositionY = y,
            ControlKeyState = controlKeyState,
        };

    private static (int X, int Y, MouseButton Button, MouseEventKind Kind, MouseKeyModifiers Modifiers) Fields(
        MouseConsoleInputEvent input) =>
        (input.X, input.Y, input.Button, input.Kind, input.Modifiers);

    private sealed record MouseParitySequence(
        string[] AnsiRecords,
        MouseEventRecord[] Win32Records,
        int TimeStepMilliseconds = 100);
}
