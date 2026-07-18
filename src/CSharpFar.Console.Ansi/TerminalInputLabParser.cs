using System.Text;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed record TerminalInputLabEvent(
    string Kind,
    byte[] RawBytes,
    bool IsKnown,
    ConsoleKeyInfo? Key = null,
    string? MouseEvent = null,
    MouseButton? MouseButton = null,
    int? ButtonCode = null,
    int? TerminalX = null,
    int? TerminalY = null,
    int? UiX = null,
    int? UiY = null,
    MouseKeyModifiers MouseModifiers = MouseKeyModifiers.None,
    EnhancedKeyEventType? KeyEventType = null,
    string? ModifierKeyName = null,
    string? Error = null);

internal sealed class TerminalInputLabParser
{
    private MouseButton _lastPressedButton = MouseButton.Left;

    public TerminalInputLabEvent Parse(ReadOnlySpan<byte> bytes)
    {
        byte[] raw = bytes.ToArray();
        if (raw.Length == 0)
            return Unknown("Unknown", raw);

        if (LooksLikeSgrMouse(raw))
            return ParseMouse(raw);

        var enhanced = EnhancedTerminalKeyParser.Parse(raw);
        if (enhanced.IsKnown)
        {
            return new TerminalInputLabEvent(
                enhanced.ModifierOnly ? "ModifierKey" : "Key",
                raw,
                true,
                enhanced.ParsedKey,
                KeyEventType: enhanced.EventType,
                ModifierKeyName: enhanced.ModifierKeyName);
        }

        try
        {
            ConsoleKeyInfo key = AnsiInputParser.ParseSingle(raw);
            bool standaloneEscape = raw.Length == 1 && raw[0] == 0x1b;
            bool known = standaloneEscape || key.Key != ConsoleKey.Escape || key.KeyChar == '\x1b' && raw.Length == 1;
            if (raw[0] != 0x1b || known)
                return new TerminalInputLabEvent("Key", raw, true, key);
        }
        catch (Exception ex) when (ex is EndOfStreamException or DecoderFallbackException)
        {
            return Unknown("Unknown", raw, ex.Message);
        }

        return Unknown("UnknownEscapeSequence", raw);
    }

    private TerminalInputLabEvent ParseMouse(byte[] raw)
    {
        if (!SgrMouseInputParser.TryParse(raw, ref _lastPressedButton, out var parsed, out string? error))
            return Unknown("MalformedMouse", raw, error);

        var mouse = parsed.Mouse;
        bool noButtonMotion = mouse.Kind == MouseEventKind.Move && (parsed.EncodedButton & 3) == 3;
        string eventName = mouse.Kind switch
        {
            MouseEventKind.Down => mouse.Button + "Down",
            MouseEventKind.Up => mouse.Button + "Up",
            MouseEventKind.Wheel => mouse.Button.ToString(),
            MouseEventKind.Move when noButtonMotion => "MoveNoButton",
            MouseEventKind.Move => "MoveWithButton",
            _ => "UnknownMouse",
        };

        return new TerminalInputLabEvent(
            "Mouse",
            raw,
            eventName != "UnknownMouse",
            MouseEvent: eventName,
            MouseButton: mouse.Button,
            ButtonCode: parsed.EncodedButton,
            TerminalX: mouse.X + 1,
            TerminalY: mouse.Y + 1,
            UiX: mouse.X,
            UiY: mouse.Y,
            MouseModifiers: mouse.Modifiers);
    }

    private static bool LooksLikeSgrMouse(IReadOnlyList<byte> bytes) =>
        bytes.Count >= 3 && bytes[0] == 0x1b && bytes[1] == '[' && bytes[2] == '<';

    private static TerminalInputLabEvent Unknown(string kind, byte[] raw, string? error = null) =>
        new(kind, raw, false, Error: error);
}
