using System.Text;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed class AnsiInputParser
{
    private const int DefaultEscapeTimeoutMilliseconds = 50;

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly int _escapeTimeoutMilliseconds;

    public AnsiInputParser(int escapeTimeoutMilliseconds = DefaultEscapeTimeoutMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(escapeTimeoutMilliseconds);
        _escapeTimeoutMilliseconds = escapeTimeoutMilliseconds;
    }

    public ConsoleKeyInfo ReadKey(Stream input, Func<bool>? inputAvailable = null) =>
        Read(new StreamAnsiInputByteReader(input, inputAvailable)).Key;

    public AnsiInputReadResult Read(Stream input, Func<bool>? inputAvailable = null) =>
        Read(new StreamAnsiInputByteReader(input, inputAvailable));

    public AnsiInputReadResult Read(IAnsiInputByteReader input)
    {
        var bytes = new List<byte>();
        byte first = ReadByte(input, bytes);
        var key = ParseFirstByte(first, input, bytes);
        return new AnsiInputReadResult(key, bytes.ToArray());
    }

    internal static ConsoleKeyInfo ParseSingle(ReadOnlySpan<byte> bytes)
    {
        using var input = new MemoryStream(bytes.ToArray());
        return new AnsiInputParser().ReadKey(input);
    }

    private ConsoleKeyInfo ParseFirstByte(
        byte first,
        IAnsiInputByteReader input,
        List<byte> bytes)
    {
        if (first == 0x1b)
        {
            return input.WaitForInput(_escapeTimeoutMilliseconds) &&
                TryReadEscapeSequence(input, bytes, out var escapeKey)
                ? escapeKey
                : MakeKey('\x1b', ConsoleKey.Escape);
        }

        if (first is (byte)'\r' or (byte)'\n')
            return MakeKey('\r', ConsoleKey.Enter);
        if (first is 0x7f or 0x08)
            return MakeKey('\b', ConsoleKey.Backspace);
        if (first == '\t')
            return MakeKey('\t', ConsoleKey.Tab);
        if (first is >= 1 and <= 26)
            return new ConsoleKeyInfo((char)('A' + first - 1), ConsoleKey.A + first - 1, shift: false, alt: false, control: true);

        return ParseUtf8(first, input, bytes);
    }

    private bool TryReadEscapeSequence(IAnsiInputByteReader input, List<byte> bytes, out ConsoleKeyInfo key)
    {
        key = default;
        if (!TryReadByte(input, bytes, out byte prefix))
            return false;

        if (prefix != '[' && prefix != 'O')
        {
            key = MakeAltKey(prefix, input, bytes);
            return true;
        }

        var sequence = new List<char>();
        while (sequence.Count < 16)
        {
            if (!input.WaitForInput(_escapeTimeoutMilliseconds))
                return false;

            if (!TryReadByte(input, bytes, out byte next))
                return false;

            sequence.Add((char)next);
            if (VirtualTerminalKeyParser.IsFinalChar((char)next) &&
                !(prefix == '[' && sequence.Count == 1 && next == '['))
            {
                return VirtualTerminalKeyParser.TryParse((char)prefix, sequence, out key);
            }
        }

        return false;
    }

    private ConsoleKeyInfo ParseUtf8(byte first, IAnsiInputByteReader input, List<byte> bytes)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = first;
        int length = Utf8SequenceLength(first);
        if (length == 1)
            return MakeCharacterKey((char)first);

        ReadExactly(input, buffer.Slice(1, length - 1), bytes);
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

    private static ConsoleKeyInfo MakeAltKey(byte first, IAnsiInputByteReader input, List<byte> bytes)
    {
        var parser = new AnsiInputParser();
        var parsed = parser.ParseFirstByte(first, input, bytes);
        return new ConsoleKeyInfo(
            parsed.KeyChar,
            parsed.Key,
            parsed.Modifiers.HasFlag(ConsoleModifiers.Shift),
            alt: true,
            parsed.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    private static ConsoleKeyInfo MakeKey(
        char keyChar,
        ConsoleKey key,
        bool shift = false,
        bool alt = false,
        bool control = false) =>
        new(keyChar, key, shift, alt, control);

    private static void ReadExactly(IAnsiInputByteReader input, Span<byte> buffer, List<byte> bytes)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = ReadByte(input, bytes);
    }

    private static byte ReadByte(IAnsiInputByteReader input, List<byte> bytes)
    {
        byte value = input.ReadByte();
        bytes.Add(value);
        return value;
    }

    private static bool TryReadByte(IAnsiInputByteReader input, List<byte> bytes, out byte value)
    {
        if (!input.TryReadByte(out value))
            return false;

        bytes.Add(value);
        return true;
    }
}

public interface IAnsiInputByteReader
{
    byte ReadByte();

    bool TryReadByte(out byte value);

    bool WaitForInput(int timeoutMilliseconds);
}

internal sealed class StreamAnsiInputByteReader : IAnsiInputByteReader
{
    private readonly Stream _input;
    private readonly Func<bool>? _inputAvailable;

    public StreamAnsiInputByteReader(Stream input, Func<bool>? inputAvailable)
    {
        _input = input;
        _inputAvailable = inputAvailable;
    }

    public byte ReadByte()
    {
        int value = _input.ReadByte();
        if (value < 0)
            throw new EndOfStreamException("Unexpected end of terminal input.");

        return (byte)value;
    }

    public bool TryReadByte(out byte value)
    {
        int read = _input.ReadByte();
        if (read < 0)
        {
            value = default;
            return false;
        }

        value = (byte)read;
        return true;
    }

    public bool WaitForInput(int timeoutMilliseconds)
    {
        if (_inputAvailable is null)
            return _input.CanSeek && _input.Position < _input.Length;

        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 <= deadline)
        {
            if (_inputAvailable())
                return true;

            Thread.Sleep(1);
        }

        return false;
    }
}

public sealed record AnsiInputReadResult(ConsoleKeyInfo Key, byte[] Bytes);
