using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed class AnsiConsoleInputParser
{
    private readonly AnsiInputParser _keyParser;
    private readonly MouseInputNormalizer _mouseNormalizer;
    private MouseButton _lastPressedButton = MouseButton.None;

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
        => TryRead(input, reportStandaloneModifiers: false, out inputEvent, out _);

    internal bool TryRead(
        IAnsiInputByteReader input,
        bool reportStandaloneModifiers,
        [NotNullWhen(true)] out ConsoleInputEvent? inputEvent,
        out bool? focusChanged)
    {
        focusChanged = null;
        AnsiInputReadResult parsed = _keyParser.Read(input);
        if (parsed.Bytes.AsSpan().SequenceEqual("\x1b[I"u8))
        {
            focusChanged = true;
            inputEvent = null;
            return false;
        }
        if (parsed.Bytes.AsSpan().SequenceEqual("\x1b[O"u8))
        {
            focusChanged = false;
            inputEvent = null;
            return false;
        }
        if (SgrMouseInputParser.TryParse(parsed.Bytes, ref _lastPressedButton, out var mouse, out _))
        {
            inputEvent = _mouseNormalizer.Normalize(mouse.Mouse);
            return true;
        }

        if (LooksLikeSgrMouse(parsed.Bytes))
        {
            inputEvent = null;
            return false;
        }

        EnhancedTerminalKeyEvent enhanced = EnhancedTerminalKeyParser.Parse(parsed.Bytes);
        if (enhanced.IsKnown)
        {
            if (enhanced.ModifierOnly)
            {
                inputEvent = reportStandaloneModifiers
                    ? new ModifierKeyConsoleInputEvent(enhanced.ParsedKey.Modifiers)
                    : null;
                return inputEvent is not null;
            }

            if (enhanced.EventType == EnhancedKeyEventType.Release)
            {
                inputEvent = null;
                return false;
            }

            inputEvent = new KeyConsoleInputEvent(enhanced.ParsedKey)
            {
                Text = enhanced.AssociatedText,
            };
            return true;
        }

        if (EnhancedTerminalKeyParser.LooksLikeKittyKeyboardSequence(parsed.Bytes))
        {
            inputEvent = null;
            return false;
        }

        inputEvent = new KeyConsoleInputEvent(parsed.Key);
        return true;
    }

    public void ResetMouseState()
    {
        _lastPressedButton = MouseButton.None;
        _mouseNormalizer.Reset();
    }

    private static bool LooksLikeSgrMouse(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 3 && bytes[0] == 0x1b && bytes[1] == '[' && bytes[2] == '<';
}
