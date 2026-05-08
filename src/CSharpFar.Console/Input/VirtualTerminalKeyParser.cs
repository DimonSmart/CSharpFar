namespace CSharpFar.Console.Input;

internal static class VirtualTerminalKeyParser
{
    internal static bool IsFinalChar(char ch) =>
        ch is >= '@' and <= '~';

    internal static bool TryParse(char prefix, IReadOnlyList<char> sequence, out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;
        if (sequence.Count == 0 || !IsFinalChar(sequence[^1]))
            return false;

        char final = sequence[^1];
        string body = new(sequence.Take(sequence.Count - 1).ToArray());

        return prefix switch
        {
            '[' => TryCreateCsiKeyInfo(body, final, out keyInfo),
            'O' => TryCreateSs3KeyInfo(body, final, out keyInfo),
            _ => false,
        };
    }

    private static bool TryCreateCsiKeyInfo(string body, char final, out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;

        if (final is 'A' or 'B' or 'C' or 'D' or 'H' or 'F')
        {
            var modifiers = ParseModifiers(body);
            var key = final switch
            {
                'A' => ConsoleKey.UpArrow,
                'B' => ConsoleKey.DownArrow,
                'C' => ConsoleKey.RightArrow,
                'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home,
                'F' => ConsoleKey.End,
                _ => ConsoleKey.NoName,
            };

            keyInfo = MakeKeyInfo(key, modifiers);
            return true;
        }

        if (final != '~')
            return false;

        var parts = body.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int code))
            return false;

        var mapped = code switch
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
        };

        if (mapped == ConsoleKey.NoName)
            return false;

        keyInfo = MakeKeyInfo(mapped, ParseModifiers(body));
        return true;
    }

    private static bool TryCreateSs3KeyInfo(string body, char final, out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;
        if (body.Length != 0)
            return false;

        var mapped = final switch
        {
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            _ => ConsoleKey.NoName,
        };

        if (mapped == ConsoleKey.NoName)
            return false;

        keyInfo = MakeKeyInfo(mapped, MouseKeyModifiers.None);
        return true;
    }

    private static MouseKeyModifiers ParseModifiers(string body)
    {
        var parts = body.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[^1], out int modifierCode))
            return MouseKeyModifiers.None;

        return modifierCode switch
        {
            2 => MouseKeyModifiers.Shift,
            3 => MouseKeyModifiers.Alt,
            4 => MouseKeyModifiers.Shift | MouseKeyModifiers.Alt,
            5 => MouseKeyModifiers.Control,
            6 => MouseKeyModifiers.Shift | MouseKeyModifiers.Control,
            7 => MouseKeyModifiers.Alt | MouseKeyModifiers.Control,
            8 => MouseKeyModifiers.Shift | MouseKeyModifiers.Alt | MouseKeyModifiers.Control,
            _ => MouseKeyModifiers.None,
        };
    }

    private static ConsoleKeyInfo MakeKeyInfo(ConsoleKey key, MouseKeyModifiers modifiers) =>
        new(
            '\0',
            key,
            (modifiers & MouseKeyModifiers.Shift) != 0,
            (modifiers & MouseKeyModifiers.Alt) != 0,
            (modifiers & MouseKeyModifiers.Control) != 0);
}
