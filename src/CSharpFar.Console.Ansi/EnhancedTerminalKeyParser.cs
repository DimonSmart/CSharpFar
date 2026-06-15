namespace CSharpFar.Console.Ansi;

internal static class EnhancedTerminalKeyParser
{
    public static EnhancedTerminalKeyEvent Parse(ReadOnlySpan<byte> bytes)
    {
        string text = DecodeAscii(bytes);
        if (!TryParseCsi(text, out var body, out char final))
            return EnhancedTerminalKeyEvent.Unknown;

        if (final is 'u' or '~')
            return ParseNumbered(body, final);

        if (final is 'A' or 'B' or 'C' or 'D' or 'H' or 'F' or 'P' or 'Q' or 'R' or 'S')
            return ParseLegacyNamed(body, final);

        return EnhancedTerminalKeyEvent.Unknown;
    }

    private static EnhancedTerminalKeyEvent ParseNumbered(string body, char final)
    {
        var fields = body.Split(';');
        if (fields.Length == 0 || !TryParseSubField(fields[0], out int keyCode))
            return EnhancedTerminalKeyEvent.Unknown;

        var modifiers = ParseModifierField(fields.Length >= 2 ? fields[1] : null);
        var key = MapKeyCode(keyCode, final);
        if (key == ConsoleKey.NoName && !TryGetModifierKeyName(keyCode, out _))
            return EnhancedTerminalKeyEvent.Unknown;

        return CreateEvent(keyCode, modifiers, key, final);
    }

    private static EnhancedTerminalKeyEvent ParseLegacyNamed(string body, char final)
    {
        var fields = string.IsNullOrEmpty(body)
            ? []
            : body.Split(';', StringSplitOptions.None);

        if (fields.Length > 0 &&
            fields[0].Length > 0 &&
            (!int.TryParse(fields[0], out int leading) || leading != 1))
        {
            return EnhancedTerminalKeyEvent.Unknown;
        }

        var modifiers = ParseModifierField(fields.Length >= 2 ? fields[1] : null);
        var key = final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            _ => ConsoleKey.NoName,
        };

        return CreateEvent(1, modifiers, key, final);
    }

    private static EnhancedTerminalKeyEvent CreateEvent(
        int keyCode,
        EnhancedModifierField modifiers,
        ConsoleKey key,
        char final)
    {
        var consoleModifiers = modifiers.ToConsoleModifiers();
        var consoleKey = new ConsoleKeyInfo(
            GetKeyChar(keyCode, key, consoleModifiers),
            key,
            consoleModifiers.HasFlag(ConsoleModifiers.Shift),
            consoleModifiers.HasFlag(ConsoleModifiers.Alt),
            consoleModifiers.HasFlag(ConsoleModifiers.Control));
        bool modifierOnly = TryGetModifierKeyName(keyCode, out string? modifierName);

        return new EnhancedTerminalKeyEvent(
            IsKnown: true,
            KeyCode: keyCode,
            ModifiersRaw: modifiers.RawValue,
            EventType: modifiers.EventType,
            Modifiers: modifiers.ToEnhancedModifiers(),
            ParsedKey: consoleKey,
            ModifierOnly: modifierOnly,
            ModifierKeyName: modifierName,
            FinalChar: final);
    }

    private static EnhancedModifierField ParseModifierField(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return new EnhancedModifierField(1, EnhancedKeyEventType.Press);

        var parts = field.Split(':', StringSplitOptions.None);
        int rawValue = int.TryParse(parts[0], out int parsedRaw) ? parsedRaw : 1;
        var eventType = parts.Length >= 2 && int.TryParse(parts[1], out int parsedEvent)
            ? parsedEvent switch
            {
                2 => EnhancedKeyEventType.Repeat,
                3 => EnhancedKeyEventType.Release,
                _ => EnhancedKeyEventType.Press,
            }
            : EnhancedKeyEventType.Press;

        return new EnhancedModifierField(rawValue, eventType);
    }

    private static bool TryParseCsi(string text, out string body, out char final)
    {
        body = "";
        final = '\0';
        if (text.Length < 3 || text[0] != '\x1b' || text[1] != '[')
            return false;

        final = text[^1];
        body = text[2..^1];
        return true;
    }

    private static bool TryParseSubField(string field, out int value)
    {
        string first = field.Split(':', StringSplitOptions.None)[0];
        return int.TryParse(first, out value);
    }

    private static ConsoleKey MapKeyCode(int keyCode, char final) =>
        final switch
        {
            '~' => keyCode switch
            {
                1 or 7 => ConsoleKey.Home,
                2 => ConsoleKey.Insert,
                3 => ConsoleKey.Delete,
                4 or 8 => ConsoleKey.End,
                5 => ConsoleKey.PageUp,
                6 => ConsoleKey.PageDown,
                11 => ConsoleKey.F1,
                12 => ConsoleKey.F2,
                13 => ConsoleKey.F3,
                14 => ConsoleKey.F4,
                15 => ConsoleKey.F5,
                17 => ConsoleKey.F6,
                18 => ConsoleKey.F7,
                19 => ConsoleKey.F8,
                20 => ConsoleKey.F9,
                21 => ConsoleKey.F10,
                23 => ConsoleKey.F11,
                24 => ConsoleKey.F12,
                _ => ConsoleKey.NoName,
            },
            'u' => keyCode switch
            {
                9 => ConsoleKey.Tab,
                13 => ConsoleKey.Enter,
                27 => ConsoleKey.Escape,
                32 => ConsoleKey.Spacebar,
                127 => ConsoleKey.Backspace,
                >= 48 and <= 57 => ConsoleKey.D0 + (keyCode - 48),
                >= 65 and <= 90 => ConsoleKey.A + (keyCode - 65),
                >= 97 and <= 122 => ConsoleKey.A + (keyCode - 97),
                _ => ConsoleKey.NoName,
            },
            _ => ConsoleKey.NoName,
        };

    private static char GetKeyChar(int keyCode, ConsoleKey key, ConsoleModifiers modifiers)
    {
        if (keyCode is >= 32 and <= 0x10ffff && !TryGetModifierKeyName(keyCode, out _))
        {
            char ch = (char)keyCode;
            return modifiers.HasFlag(ConsoleModifiers.Shift) ? char.ToUpperInvariant(ch) : ch;
        }

        return key switch
        {
            ConsoleKey.Enter => '\r',
            ConsoleKey.Tab => '\t',
            ConsoleKey.Backspace => '\b',
            ConsoleKey.Escape => '\x1b',
            _ => '\0',
        };
    }

    private static bool TryGetModifierKeyName(int keyCode, out string? name)
    {
        name = keyCode switch
        {
            57441 => "LEFT_SHIFT",
            57442 => "LEFT_CONTROL",
            57443 => "LEFT_ALT",
            57444 => "LEFT_SUPER",
            57445 => "LEFT_HYPER",
            57446 => "LEFT_META",
            57447 => "RIGHT_SHIFT",
            57448 => "RIGHT_CONTROL",
            57449 => "RIGHT_ALT",
            57450 => "RIGHT_SUPER",
            57451 => "RIGHT_HYPER",
            57452 => "RIGHT_META",
            _ => null,
        };

        return name is not null;
    }

    private static string DecodeAscii(ReadOnlySpan<byte> bytes)
    {
        char[] chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] <= 0x7f ? (char)bytes[i] : '\ufffd';
        return new string(chars);
    }
}

internal sealed record EnhancedTerminalKeyEvent(
    bool IsKnown,
    int KeyCode,
    int ModifiersRaw,
    EnhancedKeyEventType EventType,
    EnhancedModifiers Modifiers,
    ConsoleKeyInfo ParsedKey,
    bool ModifierOnly,
    string? ModifierKeyName,
    char FinalChar)
{
    public static EnhancedTerminalKeyEvent Unknown { get; } = new(
        IsKnown: false,
        KeyCode: 0,
        ModifiersRaw: 1,
        EventType: EnhancedKeyEventType.Press,
        Modifiers: EnhancedModifiers.None,
        ParsedKey: default,
        ModifierOnly: false,
        ModifierKeyName: null,
        FinalChar: '\0');
}

internal enum EnhancedKeyEventType
{
    Press,
    Repeat,
    Release,
}

[Flags]
internal enum EnhancedModifiers
{
    None = 0,
    Shift = 1 << 0,
    Alt = 1 << 1,
    Ctrl = 1 << 2,
    Super = 1 << 3,
    Hyper = 1 << 4,
    Meta = 1 << 5,
    CapsLock = 1 << 6,
    NumLock = 1 << 7,
}

internal sealed record EnhancedModifierField(int RawValue, EnhancedKeyEventType EventType)
{
    private int ActualBits => Math.Max(0, RawValue - 1);

    public ConsoleModifiers ToConsoleModifiers()
    {
        var result = default(ConsoleModifiers);
        if ((ActualBits & 1) != 0)
            result |= ConsoleModifiers.Shift;
        if ((ActualBits & 2) != 0)
            result |= ConsoleModifiers.Alt;
        if ((ActualBits & 4) != 0)
            result |= ConsoleModifiers.Control;
        return result;
    }

    public EnhancedModifiers ToEnhancedModifiers()
    {
        var result = EnhancedModifiers.None;
        if ((ActualBits & 1) != 0)
            result |= EnhancedModifiers.Shift;
        if ((ActualBits & 2) != 0)
            result |= EnhancedModifiers.Alt;
        if ((ActualBits & 4) != 0)
            result |= EnhancedModifiers.Ctrl;
        if ((ActualBits & 8) != 0)
            result |= EnhancedModifiers.Super;
        if ((ActualBits & 16) != 0)
            result |= EnhancedModifiers.Hyper;
        if ((ActualBits & 32) != 0)
            result |= EnhancedModifiers.Meta;
        if ((ActualBits & 64) != 0)
            result |= EnhancedModifiers.CapsLock;
        if ((ActualBits & 128) != 0)
            result |= EnhancedModifiers.NumLock;
        return result;
    }
}
