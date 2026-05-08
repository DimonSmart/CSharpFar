using System.Runtime.InteropServices;
using CSharpFar.Console.Win32;

namespace CSharpFar.Tests;

public sealed class Win32ConsoleInputLayoutTests
{
    [Fact]
    public void KeyEventRecord_MatchesWin32Layout()
    {
        Assert.Equal(16, Marshal.SizeOf<KeyEventRecord>());
        Assert.Equal(0, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.KeyDown)));
        Assert.Equal(4, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.RepeatCount)));
        Assert.Equal(6, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.VirtualKeyCode)));
        Assert.Equal(8, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.VirtualScanCode)));
        Assert.Equal(10, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.UnicodeChar)));
        Assert.Equal(12, OffsetOf<KeyEventRecord>(nameof(KeyEventRecord.ControlKeyState)));
    }

    [Fact]
    public void InputRecord_OverlapsEventUnionAtWin32Offset()
    {
        Assert.Equal(20, Marshal.SizeOf<InputRecord>());
        Assert.Equal(0, OffsetOf<InputRecord>(nameof(InputRecord.EventType)));
        Assert.Equal(4, OffsetOf<InputRecord>(nameof(InputRecord.KeyEvent)));
        Assert.Equal(4, OffsetOf<InputRecord>(nameof(InputRecord.MouseEvent)));
    }

    private static int OffsetOf<T>(string fieldName) =>
        Marshal.OffsetOf<T>(fieldName).ToInt32();
}
