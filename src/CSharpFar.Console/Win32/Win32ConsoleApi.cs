using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CSharpFar.Console.Win32;

[SupportedOSPlatform("windows")]
internal static class Win32ConsoleApi
{
    public const uint ENABLE_PROCESSED_INPUT = 0x0001;
    public const uint ENABLE_INSERT_MODE = 0x0020;
    public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    public const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    private const ushort KEY_EVENT = 0x0001;
    private const uint RIGHT_ALT_PRESSED = 0x0001;
    private const uint LEFT_ALT_PRESSED = 0x0002;
    private const uint RIGHT_CTRL_PRESSED = 0x0004;
    private const uint LEFT_CTRL_PRESSED = 0x0008;
    private const uint SHIFT_PRESSED = 0x0010;

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", ExactSpelling = true, SetLastError = true)]
    private static extern bool ReadConsoleInput(
        IntPtr hConsoleInput,
        [Out] InputRecord[] lpBuffer,
        uint nLength,
        out uint lpNumberOfEventsRead);

    [DllImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", ExactSpelling = true, SetLastError = true)]
    private static extern bool ReadConsoleOutput(
        IntPtr hConsoleOutput,
        [Out] CharInfo[] lpBuffer,
        Coord dwBufferSize,
        Coord dwBufferCoord,
        ref SmallRect lpReadRegion);

    [DllImport("kernel32.dll", EntryPoint = "WriteConsoleOutputW", ExactSpelling = true, SetLastError = true)]
    private static extern bool WriteConsoleOutput(
        IntPtr hConsoleOutput,
        [In] CharInfo[] lpBuffer,
        Coord dwBufferSize,
        Coord dwBufferCoord,
        ref SmallRect lpWriteRegion);

    public static IntPtr GetConsoleInputHandle() => GetStdHandle(STD_INPUT_HANDLE);
    public static IntPtr GetConsoleOutputHandle() => GetStdHandle(STD_OUTPUT_HANDLE);

    public static bool TryGetConsoleMode(IntPtr handle, out uint mode) =>
        GetConsoleMode(handle, out mode);

    public static bool TrySetConsoleMode(IntPtr handle, uint mode) =>
        SetConsoleMode(handle, mode);

    public static bool TryReadKey(IntPtr inputHandle, bool intercept, out ConsoleKeyInfo keyInfo)
    {
        var buffer = new InputRecord[1];

        while (ReadConsoleInput(inputHandle, buffer, 1, out uint read) && read == 1)
        {
            var record = buffer[0];
            if (record.EventType != KEY_EVENT || !record.KeyEvent.KeyDown)
                continue;

            var modifiers = GetModifiers(record.KeyEvent.ControlKeyState);
            var key = Enum.IsDefined(typeof(ConsoleKey), (int)record.KeyEvent.VirtualKeyCode)
                ? (ConsoleKey)record.KeyEvent.VirtualKeyCode
                : ConsoleKey.NoName;

            keyInfo = new ConsoleKeyInfo(
                record.KeyEvent.UnicodeChar,
                key,
                (modifiers & ConsoleModifiers.Shift) != 0,
                (modifiers & ConsoleModifiers.Alt) != 0,
                (modifiers & ConsoleModifiers.Control) != 0);

            if (!intercept && record.KeyEvent.UnicodeChar != '\0')
                global::System.Console.Write(record.KeyEvent.UnicodeChar);

            return true;
        }

        keyInfo = default;
        return false;
    }

    private static ConsoleModifiers GetModifiers(uint controlKeyState)
    {
        var result = default(ConsoleModifiers);

        if ((controlKeyState & SHIFT_PRESSED) != 0)
            result |= ConsoleModifiers.Shift;
        if ((controlKeyState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) != 0)
            result |= ConsoleModifiers.Alt;
        if ((controlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0)
            result |= ConsoleModifiers.Control;

        return result;
    }

    public static CharInfo[]? ReadRegion(IntPtr handle, SmallRect region)
    {
        int w = region.Right - region.Left + 1;
        int h = region.Bottom - region.Top + 1;
        if (w <= 0 || h <= 0)
            return null;

        var buffer = new CharInfo[h * w];
        var size = new Coord { X = (short)w, Y = (short)h };
        var coord = new Coord { X = 0, Y = 0 };
        var readRegion = region;

        ReadConsoleOutput(handle, buffer, size, coord, ref readRegion);
        return buffer;
    }

    public static void WriteRegion(IntPtr handle, CharInfo[] buffer, SmallRect region)
    {
        int w = region.Right - region.Left + 1;
        int h = region.Bottom - region.Top + 1;
        var size = new Coord { X = (short)w, Y = (short)h };
        var coord = new Coord { X = 0, Y = 0 };
        var writeRegion = region;

        WriteConsoleOutput(handle, buffer, size, coord, ref writeRegion);
    }

    // Windows color attribute = foreground nibble | (background nibble << 4)
    // ConsoleColor enum values 0-15 map directly to Windows color attribute nibbles.
    public static short MakeAttributes(ConsoleColor fg, ConsoleColor bg) =>
        (short)((int)fg | ((int)bg << 4));

    public static (ConsoleColor fg, ConsoleColor bg) SplitAttributes(short attributes) =>
        ((ConsoleColor)(attributes & 0xF), (ConsoleColor)((attributes >> 4) & 0xF));
}

[StructLayout(LayoutKind.Sequential)]
internal struct Coord
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SmallRect
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
internal struct CharInfo
{
    [FieldOffset(0)] public char UnicodeChar;
    [FieldOffset(2)] public short Attributes;
}

[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
internal struct InputRecord
{
    [FieldOffset(0)] public ushort EventType;
    [FieldOffset(4)] public KeyEventRecord KeyEvent;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct KeyEventRecord
{
    [MarshalAs(UnmanagedType.Bool)] public bool KeyDown;
    public ushort RepeatCount;
    public ushort VirtualKeyCode;
    public ushort VirtualScanCode;
    public char UnicodeChar;
    public uint ControlKeyState;
}
