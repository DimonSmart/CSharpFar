using CSharpFar.Console.Input;

namespace CSharpFar.Console.Tests;

public class MacTextEditingShortcutTests
{
    [Theory]
    [InlineData(ConsoleKey.A)]
    [InlineData(ConsoleKey.C)]
    [InlineData(ConsoleKey.V)]
    [InlineData(ConsoleKey.X)]
    [InlineData(ConsoleKey.Z)]
    public void SuperCanBeMappedOnlyByTextOwner(ConsoleKey key)
    {
        var evt = new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', key, false, false, false))
        {
            ExtendedModifiers = TerminalExtendedModifiers.Super
        };
        Assert.True(evt.TryGetMacTextEditingShortcut(out var shortcut));
        Assert.True(shortcut.Modifiers.HasFlag(ConsoleModifiers.Control));
        Assert.False(evt.Key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void UnknownCommandMustNotBeControl()
    {
        var evt = new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Q, false, false, false))
        {
            ExtendedModifiers = TerminalExtendedModifiers.Super
        };
        Assert.False(evt.TryGetMacTextEditingShortcut(out _));
    }

    [Fact]
    public void CommandShiftZMapsToCtrlShiftZ()
    {
        var evt = new KeyConsoleInputEvent(new ConsoleKeyInfo('\0', ConsoleKey.Z, true, false, false))
        {
            ExtendedModifiers = TerminalExtendedModifiers.Super
        };
        Assert.True(evt.TryGetMacTextEditingShortcut(out var shortcut));
        Assert.Equal(ConsoleModifiers.Control | ConsoleModifiers.Shift, shortcut.Modifiers);
    }
}
