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

    [Fact]
    public void EquivalentPhysicalInput_ProducesEquivalentSemanticStreams()
    {
        foreach (var (ansiRecords, win32Records) in Sequences())
        {
            long timestamp = 1_000;
            var ansi = new AnsiConsoleInputParser(50, () => timestamp);
            var ansiEvents = ParseAnsi(ansi, ansiRecords, ref timestamp);
            timestamp = 1_000;
            var win32 = new Win32MouseInputParser();
            var normalizer = new MouseInputNormalizer(() => timestamp);
            var win32Events = win32Records
                .Select(record => win32.Parse(record, windowLeft: 11, windowTop: 26))
                .Where(mouse => mouse is not null)
                .Select(mouse => normalizer.Normalize(mouse!))
                .ToArray();

            Assert.Equal(ansiEvents.Select(Fields), win32Events.Select(Fields));
        }
    }

    private static IEnumerable<(string[] AnsiRecords, MouseEventRecord[] Win32Records)> Sequences()
    {
        yield return (["\u001b[<0;10;5M", "\u001b[<0;10;5m"], [Record(Left), Record(0)]);
        yield return (["\u001b[<2;10;5M", "\u001b[<0;10;5m"], [Record(Right), Record(0)]);
        yield return (["\u001b[<1;10;5M", "\u001b[<0;10;5m"], [Record(Middle), Record(0)]);
        yield return (
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m", "\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [Record(Left), Record(0), Record(Left), Record(0)]);
        yield return (
            ["\u001b[<0;10;5M", "\u001b[<32;11;5M", "\u001b[<32;12;5M", "\u001b[<0;12;5m"],
            [Record(Left), Record(Left, MouseMoved, 21), Record(Left, MouseMoved, 22), Record(0, x: 22)]);
        yield return (["\u001b[<64;10;5M", "\u001b[<65;10;5M"], [Record(120u << 16, MouseWheeled), Record(0xFF88u << 16, MouseWheeled)]);
        yield return (["\u001b[<20;10;5M", "\u001b[<20;10;5m"], [Record(Left, controlKeyState: 0x0014), Record(0, controlKeyState: 0x0014)]);
    }

    private static MouseConsoleInputEvent[] ParseAnsi(
        AnsiConsoleInputParser parser,
        IEnumerable<string> records,
        ref long timestamp)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(string.Concat(records)));
        var reader = new StreamAnsiInputByteReader(input, null);
        var result = new List<MouseConsoleInputEvent>();
        foreach (string _ in records)
        {
            Assert.True(parser.TryRead(reader, out var inputEvent));
            result.Add(Assert.IsType<MouseConsoleInputEvent>(inputEvent));
            timestamp += 100;
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
}
