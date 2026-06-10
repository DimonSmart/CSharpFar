using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Console;
using CSharpFar.Console.Win32;

namespace CSharpFar.Tests;

public sealed class Win32ConsoleInputLayoutTests
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftMenu = 0xA4;
    private const ushort VkRightMenu = 0xA5;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftCtrlPressed = 0x0008;

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

    [Theory]
    [InlineData(VkShift, ConsoleModifiers.Shift)]
    [InlineData(VkControl, ConsoleModifiers.Control)]
    [InlineData(VkMenu, ConsoleModifiers.Alt)]
    [InlineData(VkLeftMenu, ConsoleModifiers.Alt)]
    [InlineData(VkRightMenu, ConsoleModifiers.Alt)]
    [SupportedOSPlatform("windows")]
    public void GetKeyEventModifiers_ModifierKeyDownIncludesVirtualKey(
        ushort virtualKeyCode,
        ConsoleModifiers expectedModifier)
    {
        var keyEvent = new KeyEventRecord
        {
            KeyDown = 1,
            VirtualKeyCode = virtualKeyCode,
            ControlKeyState = 0,
        };

        Assert.Equal(expectedModifier, Win32ConsoleApi.GetKeyEventModifiers(keyEvent));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void GetKeyEventModifiers_ModifierKeyUpDoesNotAddReleasedVirtualKey()
    {
        var keyEvent = new KeyEventRecord
        {
            KeyDown = 0,
            VirtualKeyCode = VkMenu,
            ControlKeyState = 0,
        };

        Assert.Equal(default(ConsoleModifiers), Win32ConsoleApi.GetKeyEventModifiers(keyEvent));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void GetKeyEventModifiers_RightAltWithSyntheticLeftControlIsAlt()
    {
        var keyEvent = new KeyEventRecord
        {
            KeyDown = 1,
            VirtualKeyCode = VkRightMenu,
            ControlKeyState = RightAltPressed | LeftCtrlPressed,
        };

        Assert.Equal(ConsoleModifiers.Alt, Win32ConsoleApi.GetKeyEventModifiers(keyEvent));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void TryCreateModifierStateChangeEvent_EmitsOnlyWhenStateChanges()
    {
        var lastState = default(ConsoleModifiers);

        bool changed = Win32ModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref lastState,
            ConsoleModifiers.Alt,
            out var inputEvent);

        Assert.True(changed);
        Assert.NotNull(inputEvent);
        Assert.Equal(ConsoleModifiers.Alt, inputEvent.Modifiers);
        Assert.Equal(ConsoleModifiers.Alt, lastState);

        changed = Win32ModifierKeyTracker.TryCreateModifierStateChangeEvent(
            ref lastState,
            ConsoleModifiers.Alt,
            out inputEvent);

        Assert.False(changed);
        Assert.Null(inputEvent);
        Assert.Equal(ConsoleModifiers.Alt, lastState);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void GetApplicationInputMode_UsesRawInputAndDisablesConsoleTextSelectionShortcuts()
    {
        uint originalMode =
            Win32ConsoleApi.ENABLE_PROCESSED_INPUT |
            Win32ConsoleApi.ENABLE_LINE_INPUT |
            Win32ConsoleApi.ENABLE_ECHO_INPUT |
            Win32ConsoleApi.ENABLE_INSERT_MODE |
            Win32ConsoleApi.ENABLE_QUICK_EDIT_MODE |
            Win32ConsoleApi.ENABLE_VIRTUAL_TERMINAL_INPUT;

        uint appMode = SystemConsoleDriver.GetApplicationInputMode(originalMode);

        Assert.True((appMode & Win32ConsoleApi.ENABLE_EXTENDED_FLAGS) != 0);
        Assert.True((appMode & Win32ConsoleApi.ENABLE_MOUSE_INPUT) != 0);
        Assert.True((appMode & Win32ConsoleApi.ENABLE_WINDOW_INPUT) != 0);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_PROCESSED_INPUT);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_LINE_INPUT);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_ECHO_INPUT);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_INSERT_MODE);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_QUICK_EDIT_MODE);
        Assert.Equal(0u, appMode & Win32ConsoleApi.ENABLE_VIRTUAL_TERMINAL_INPUT);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void GetChildProcessInputMode_EnablesProcessedInput()
    {
        uint originalMode =
            Win32ConsoleApi.ENABLE_LINE_INPUT |
            Win32ConsoleApi.ENABLE_ECHO_INPUT;

        uint childMode = SystemConsoleDriver.GetChildProcessInputMode(originalMode);

        Assert.True((childMode & Win32ConsoleApi.ENABLE_PROCESSED_INPUT) != 0);
        Assert.True((childMode & Win32ConsoleApi.ENABLE_LINE_INPUT) != 0);
        Assert.True((childMode & Win32ConsoleApi.ENABLE_ECHO_INPUT) != 0);
    }

    private static int OffsetOf<T>(string fieldName) =>
        Marshal.OffsetOf<T>(fieldName).ToInt32();
}
