using System.Text;

namespace CSharpFar.Console.Ansi;

internal sealed class AnsiInputParser
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();

    public ConsoleKeyInfo ReadKey(Stream input)
    {
        Span<byte> first = stackalloc byte[1];
        ReadExactly(input, first);
        return ParseFirstByte(first[0], input);
    }

    internal static ConsoleKeyInfo ParseSingle(ReadOnlySpan<byte> bytes)
    {
        using var input = new MemoryStream(bytes.ToArray());
        return new AnsiInputParser().ReadKey(input);
    }

    private ConsoleKeyInfo ParseFirstByte(byte first, Stream input)
    {
        if (first == 0x1b)
            return TryReadEscapeSequence(input, out var escapeKey) ? escapeKey : MakeKey('\x1b', ConsoleKey.Escape);
        if (first is (byte)'\r' or (byte)'\n')
            return MakeKey('\r', ConsoleKey.Enter);
        if (first is 0x7f or 0x08)
            return MakeKey('\b', ConsoleKey.Backspace);
        if (first == '\t')
            return MakeKey('\t', ConsoleKey.Tab);
        if (first is >= 1 and <= 26)
            return new ConsoleKeyInfo((char)('A' + first - 1), ConsoleKey.A + first - 1, shift: false, alt: false, control: true);

        return ParseUtf8(first, input);
    }

    private bool TryReadEscapeSequence(Stream input, out ConsoleKeyInfo key)
    {
        key = default;
        int prefix = input.ReadByte();
        if (prefix < 0)
            return false;

        if (prefix != '[' && prefix != 'O')
            return false;

        var sequence = new List<byte>();
        while (sequence.Count < 16)
        {
            int next = input.ReadByte();
            if (next < 0)
                return false;

            sequence.Add((byte)next);
            if (next is >= '@' and <= '~')
                return TryMapEscape((char)prefix, Encoding.ASCII.GetString(sequence.ToArray()), out key);
        }

        return false;
    }

    private static bool TryMapEscape(char prefix, string sequence, out ConsoleKeyInfo key)
    {
        key = default;
        if (prefix == 'O' && sequence is "H" or "F")
        {
            key = MakeKey('\0', sequence == "H" ? ConsoleKey.Home : ConsoleKey.End);
            return true;
        }

        if (prefix != '[')
            return false;

        var mapped = sequence switch
        {
            "A" => ConsoleKey.UpArrow,
            "B" => ConsoleKey.DownArrow,
            "C" => ConsoleKey.RightArrow,
            "D" => ConsoleKey.LeftArrow,
            "H" or "1~" or "7~" => ConsoleKey.Home,
            "F" or "4~" or "8~" => ConsoleKey.End,
            "2~" => ConsoleKey.Insert,
            "3~" => ConsoleKey.Delete,
            "5~" => ConsoleKey.PageUp,
            "6~" => ConsoleKey.PageDown,
            _ => ConsoleKey.NoName,
        };

        if (mapped == ConsoleKey.NoName)
            return false;

        key = MakeKey('\0', mapped);
        return true;
    }

    private ConsoleKeyInfo ParseUtf8(byte first, Stream input)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = first;
        int length = Utf8SequenceLength(first);
        if (length == 1)
            return MakeCharacterKey((char)first);

        ReadExactly(input, buffer.Slice(1, length - 1));
        Span<char> chars = stackalloc char[2];
        _decoder.Convert(buffer[..length], chars, flush: true, out _, out int charsUsed, out _);
        return MakeCharacterKey(charsUsed > 0 ? chars[0] : '\0');
    }

    private static int Utf8SequenceLength(byte first) =>
        first switch
        {
            < 0x80 => 1,
            >= 0xC0 and < 0xE0 => 2,
            >= 0xE0 and < 0xF0 => 3,
            >= 0xF0 and < 0xF8 => 4,
            _ => 1,
        };

    private static ConsoleKeyInfo MakeCharacterKey(char ch)
    {
        var key = char.ToUpperInvariant(ch) switch
        {
            >= 'A' and <= 'Z' => (ConsoleKey)Enum.Parse(typeof(ConsoleKey), char.ToUpperInvariant(ch).ToString()),
            >= '0' and <= '9' => ConsoleKey.D0 + (ch - '0'),
            ' ' => ConsoleKey.Spacebar,
            _ => ConsoleKey.NoName,
        };

        return MakeKey(ch, key);
    }

    private static ConsoleKeyInfo MakeKey(char keyChar, ConsoleKey key) =>
        new(keyChar, key, shift: false, alt: false, control: false);

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of terminal input.");
            offset += read;
        }
    }
}
