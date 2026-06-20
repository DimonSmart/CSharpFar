using CSharpFar.Console.Input;
using System.Diagnostics.CodeAnalysis;

namespace CSharpFar.Console.Ansi;

internal sealed record SgrMouseInputParseResult(
    MouseConsoleInputEvent Mouse,
    int EncodedButton,
    char Final);

internal static class SgrMouseInputParser
{
    public static bool TryParse(
        IReadOnlyList<byte> bytes,
        ref MouseButton lastPressedButton,
        [NotNullWhen(true)] out SgrMouseInputParseResult? result,
        out string? error)
    {
        result = null;
        error = null;

        if (bytes.Count < 4 || bytes[0] != 0x1b || bytes[1] != '[' || bytes[2] != '<')
            return false;

        char final = (char)bytes[^1];
        if (final is not ('M' or 'm'))
        {
            error = "SGR mouse sequence must end with M or m.";
            return false;
        }

        int firstSeparator = IndexOf(bytes, (byte)';', 3);
        int secondSeparator = firstSeparator < 0 ? -1 : IndexOf(bytes, (byte)';', firstSeparator + 1);
        if (firstSeparator < 0 || secondSeparator < 0 || secondSeparator >= bytes.Count - 1)
        {
            error = "SGR mouse sequence must contain Cb, Px, and Py.";
            return false;
        }

        if (!TryParseNumber(bytes, 3, firstSeparator, out int encodedButton) ||
            !TryParseNumber(bytes, firstSeparator + 1, secondSeparator, out int terminalX) ||
            !TryParseNumber(bytes, secondSeparator + 1, bytes.Count - 1, out int terminalY))
        {
            error = "SGR mouse sequence contains a non-numeric or overflowing field.";
            return false;
        }

        if (terminalX < 1 || terminalY < 1)
        {
            error = "SGR mouse coordinates must be one-based positive values.";
            return false;
        }

        bool wheel = (encodedButton & 64) != 0;
        bool motion = (encodedButton & 32) != 0;
        int buttonCode = encodedButton & 3;

        MouseButton button;
        MouseEventKind kind;
        if (wheel)
        {
            button = (encodedButton & 1) == 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            kind = MouseEventKind.Wheel;
        }
        else if (final == 'm')
        {
            button = lastPressedButton;
            kind = MouseEventKind.Up;
        }
        else
        {
            if (!TryMapButton(buttonCode, out button))
            {
                if (!motion)
                {
                    error = $"Unsupported SGR mouse button code: {buttonCode}.";
                    return false;
                }

                button = lastPressedButton;
            }

            kind = motion ? MouseEventKind.Move : MouseEventKind.Down;
            lastPressedButton = button;
        }

        var modifiers = MouseKeyModifiers.None;
        if ((encodedButton & 4) != 0)
            modifiers |= MouseKeyModifiers.Shift;
        if ((encodedButton & 8) != 0)
            modifiers |= MouseKeyModifiers.Alt;
        if ((encodedButton & 16) != 0)
            modifiers |= MouseKeyModifiers.Control;

        var mouse = new MouseConsoleInputEvent(
            terminalX - 1,
            terminalY - 1,
            button,
            kind,
            modifiers);
        result = new SgrMouseInputParseResult(mouse, encodedButton, final);
        return true;
    }

    private static int IndexOf(IReadOnlyList<byte> bytes, byte value, int start)
    {
        for (int i = start; i < bytes.Count; i++)
        {
            if (bytes[i] == value)
                return i;
        }

        return -1;
    }

    private static bool TryParseNumber(IReadOnlyList<byte> bytes, int start, int end, out int value)
    {
        value = 0;
        if (start >= end)
            return false;

        for (int i = start; i < end; i++)
        {
            byte digit = bytes[i];
            if (digit is < (byte)'0' or > (byte)'9')
                return false;

            try
            {
                value = checked(value * 10 + digit - '0');
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryMapButton(int buttonCode, out MouseButton button)
    {
        button = buttonCode switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => default,
        };
        return buttonCode is >= 0 and <= 2;
    }
}
