using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CSharpFar.Console.Win32;

[SupportedOSPlatform("windows")]
internal static class Win32ConsoleApi
{
    private const int STD_OUTPUT_HANDLE = -11;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

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

    public static IntPtr GetConsoleOutputHandle() => GetStdHandle(STD_OUTPUT_HANDLE);

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
