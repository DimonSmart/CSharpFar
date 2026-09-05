using System.Text;
using CSharpFar.Console.Ansi;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi.Tests;

public sealed class MouseBackendParityTests
{
    [Fact]
    public void AnsiPhysicalInput_ProducesBackendIndependentSemanticStream()
    {
        foreach (var sequence in Sequences())
        {
            long timestamp = 1_000;
            var ansi = new AnsiConsoleInputParser(50, () => timestamp);
            var ansiEvents = ParseAnsi(ansi, sequence.AnsiRecords, sequence.TimeStepMilliseconds, ref timestamp);

            Assert.Equal(sequence.Expected, ansiEvents.Select(Fields));
        }
    }

    private static IEnumerable<MouseParitySequence> Sequences()
    {
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [E(9, 4, MouseButton.Left, MouseEventKind.Down), E(9, 4, MouseButton.Left, MouseEventKind.Up)]);
        yield return new(
            ["\u001b[<2;10;5M", "\u001b[<2;10;5m"],
            [E(9, 4, MouseButton.Right, MouseEventKind.Down), E(9, 4, MouseButton.Right, MouseEventKind.Up)]);
        yield return new(
            ["\u001b[<1;10;5M", "\u001b[<1;10;5m"],
            [E(9, 4, MouseButton.Middle, MouseEventKind.Down), E(9, 4, MouseButton.Middle, MouseEventKind.Up)]);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m", "\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [
                E(9, 4, MouseButton.Left, MouseEventKind.Down),
                E(9, 4, MouseButton.Left, MouseEventKind.Up),
                E(9, 4, MouseButton.Left, MouseEventKind.DoubleClick),
                E(9, 4, MouseButton.Left, MouseEventKind.Up),
            ]);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<0;10;5m", "\u001b[<0;10;5M", "\u001b[<0;10;5m"],
            [
                E(9, 4, MouseButton.Left, MouseEventKind.Down),
                E(9, 4, MouseButton.Left, MouseEventKind.Up),
                E(9, 4, MouseButton.Left, MouseEventKind.Down),
                E(9, 4, MouseButton.Left, MouseEventKind.Up),
            ],
            TimeStepMilliseconds: 600);
        yield return new(
            ["\u001b[<0;10;5M", "\u001b[<32;11;5M", "\u001b[<32;12;5M", "\u001b[<0;12;5m"],
            [
                E(9, 4, MouseButton.Left, MouseEventKind.Down),
                E(10, 4, MouseButton.Left, MouseEventKind.Move),
                E(11, 4, MouseButton.Left, MouseEventKind.Move),
                E(11, 4, MouseButton.Left, MouseEventKind.Up),
            ]);
        yield return new(
            ["\u001b[<35;10;5M"],
            [E(9, 4, MouseButton.None, MouseEventKind.Move)]);
        yield return new(
            ["\u001b[<2;10;5M", "\u001b[<34;11;5M", "\u001b[<2;11;5m", "\u001b[<35;12;5M"],
            [
                E(9, 4, MouseButton.Right, MouseEventKind.Down),
                E(10, 4, MouseButton.Right, MouseEventKind.Move),
                E(10, 4, MouseButton.Right, MouseEventKind.Up),
                E(11, 4, MouseButton.None, MouseEventKind.Move),
            ]);
        yield return new(
            ["\u001b[<64;10;5M", "\u001b[<65;10;5M"],
            [E(9, 4, MouseButton.WheelUp, MouseEventKind.Wheel), E(9, 4, MouseButton.WheelDown, MouseEventKind.Wheel)]);
        yield return new(
            ["\u001b[<6;10;5M", "\u001b[<6;10;5m"],
            [
                E(9, 4, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.Shift),
                E(9, 4, MouseButton.Right, MouseEventKind.Up, MouseKeyModifiers.Shift),
            ]);
        yield return new(
            ["\u001b[<10;10;5M", "\u001b[<10;10;5m"],
            [
                E(9, 4, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.Alt),
                E(9, 4, MouseButton.Right, MouseEventKind.Up, MouseKeyModifiers.Alt),
            ]);
        yield return new(
            ["\u001b[<18;10;5M", "\u001b[<18;10;5m"],
            [
                E(9, 4, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.Control),
                E(9, 4, MouseButton.Right, MouseEventKind.Up, MouseKeyModifiers.Control),
            ]);
        yield return new(
            ["\u001b[<22;10;5M", "\u001b[<22;10;5m"],
            [
                E(9, 4, MouseButton.Right, MouseEventKind.Down, MouseKeyModifiers.Shift | MouseKeyModifiers.Control),
                E(9, 4, MouseButton.Right, MouseEventKind.Up, MouseKeyModifiers.Shift | MouseKeyModifiers.Control),
            ]);
    }

    private static MouseConsoleInputEvent[] ParseAnsi(
        AnsiConsoleInputParser parser,
        IEnumerable<string> records,
        int timeStepMilliseconds,
        ref long timestamp)
    {
        string[] recordArray = records.ToArray();
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(string.Concat(recordArray)));
        var reader = new StreamAnsiInputByteReader(input, null);
        var result = new List<MouseConsoleInputEvent>();
        foreach (string _ in recordArray)
        {
            Assert.True(parser.TryRead(reader, out var inputEvent));
            result.Add(Assert.IsType<MouseConsoleInputEvent>(inputEvent));
            timestamp += timeStepMilliseconds;
        }

        return [.. result];
    }

    private static ExpectedEvent E(
        int x,
        int y,
        MouseButton button,
        MouseEventKind kind,
        MouseKeyModifiers modifiers = MouseKeyModifiers.None) =>
        new(x, y, button, kind, modifiers);

    private static ExpectedEvent Fields(MouseConsoleInputEvent input) =>
        new(input.X, input.Y, input.Button, input.Kind, input.Modifiers);

    private sealed record MouseParitySequence(
        string[] AnsiRecords,
        ExpectedEvent[] Expected,
        int TimeStepMilliseconds = 100);

    private sealed record ExpectedEvent(
        int X,
        int Y,
        MouseButton Button,
        MouseEventKind Kind,
        MouseKeyModifiers Modifiers);
}
