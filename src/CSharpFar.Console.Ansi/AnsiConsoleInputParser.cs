using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed class AnsiConsoleInputParser
{
    private const long DoubleClickIntervalMilliseconds = 500;

    private readonly AnsiInputParser _keyParser;
    private readonly Func<long> _getTimestampMilliseconds;
    private MouseButton _lastPressedButton = MouseButton.Left;
    private MouseClick? _lastClick;

    public AnsiConsoleInputParser(int escapeTimeoutMilliseconds = 50)
        : this(escapeTimeoutMilliseconds, () => Environment.TickCount64)
    {
    }

    internal AnsiConsoleInputParser(
        int escapeTimeoutMilliseconds,
        Func<long> getTimestampMilliseconds)
    {
        _keyParser = new AnsiInputParser(escapeTimeoutMilliseconds);
        _getTimestampMilliseconds = getTimestampMilliseconds ??
            throw new ArgumentNullException(nameof(getTimestampMilliseconds));
    }

    public bool TryRead(
        IAnsiInputByteReader input,
        [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        AnsiInputReadResult parsed = _keyParser.Read(input);
        if (SgrMouseInputParser.TryParse(parsed.Bytes, ref _lastPressedButton, out var mouse, out _))
        {
            inputEvent = DetectDoubleClick(mouse.Mouse);
            return true;
        }

        if (LooksLikeSgrMouse(parsed.Bytes))
        {
            inputEvent = null;
            return false;
        }

        inputEvent = new KeyConsoleInputEvent(parsed.Key);
        return true;
    }

    private MouseConsoleInputEvent DetectDoubleClick(MouseConsoleInputEvent mouse)
    {
        if (mouse.Kind is MouseEventKind.Move or MouseEventKind.Wheel)
        {
            _lastClick = null;
            return mouse;
        }

        if (mouse.Kind != MouseEventKind.Down)
            return mouse;

        long timestamp = _getTimestampMilliseconds();
        if (_lastClick is { } previous &&
            previous.Button == mouse.Button &&
            previous.X == mouse.X &&
            previous.Y == mouse.Y &&
            previous.Modifiers == mouse.Modifiers &&
            timestamp - previous.TimestampMilliseconds is >= 0 and <= DoubleClickIntervalMilliseconds)
        {
            _lastClick = null;
            return mouse with { Kind = MouseEventKind.DoubleClick };
        }

        _lastClick = new MouseClick(
            mouse.Button,
            mouse.X,
            mouse.Y,
            mouse.Modifiers,
            timestamp);
        return mouse;
    }

    private static bool LooksLikeSgrMouse(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 3 && bytes[0] == 0x1b && bytes[1] == '[' && bytes[2] == '<';

    private sealed record MouseClick(
        MouseButton Button,
        int X,
        int Y,
        MouseKeyModifiers Modifiers,
        long TimestampMilliseconds);
}
