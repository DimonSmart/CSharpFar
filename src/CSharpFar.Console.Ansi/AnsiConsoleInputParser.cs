using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed class AnsiConsoleInputParser
{
    private readonly AnsiInputParser _keyParser;
    private readonly MouseInputNormalizer _mouseNormalizer;
    private MouseButton _lastPressedButton = MouseButton.Left;

    public AnsiConsoleInputParser(int escapeTimeoutMilliseconds = 50)
        : this(escapeTimeoutMilliseconds, () => Environment.TickCount64)
    {
    }

    internal AnsiConsoleInputParser(
        int escapeTimeoutMilliseconds,
        Func<long> getTimestampMilliseconds)
    {
        _keyParser = new AnsiInputParser(escapeTimeoutMilliseconds);
        _mouseNormalizer = new MouseInputNormalizer(getTimestampMilliseconds);
    }

    public bool TryRead(
        IAnsiInputByteReader input,
        [NotNullWhen(true)] out ConsoleInputEvent? inputEvent)
    {
        AnsiInputReadResult parsed = _keyParser.Read(input);
        if (SgrMouseInputParser.TryParse(parsed.Bytes, ref _lastPressedButton, out var mouse, out _))
        {
            if (mouse.Mouse.Kind == MouseEventKind.Move && (mouse.EncodedButton & 3) == 3)
            {
                inputEvent = null;
                return false;
            }

            inputEvent = _mouseNormalizer.Normalize(mouse.Mouse);
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

    public void ResetMouseState()
    {
        _lastPressedButton = MouseButton.Left;
        _mouseNormalizer.Reset();
    }

    private static bool LooksLikeSgrMouse(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 3 && bytes[0] == 0x1b && bytes[1] == '[' && bytes[2] == '<';
}
