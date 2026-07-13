using CSharpFar.Console.Input;

namespace CSharpFar.Console.Win32;

internal sealed class Win32MouseInputParser
{
    private const uint FromLeftFirstButtonPressed = 0x0001;
    private const uint RightmostButtonPressed = 0x0002;
    private const uint FromLeftSecondButtonPressed = 0x0004;
    private const uint MouseMoved = 0x0001;
    private const uint DoubleClick = 0x0002;
    private const uint MouseWheeled = 0x0004;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;

    private uint _pressedButtons;

    public MouseConsoleInputEvent? Parse(
        MouseEventRecord record,
        int windowLeft,
        int windowTop)
    {
        var mods = GetModifiers(record.ControlKeyState);
        int x = record.MousePositionX - windowLeft;
        int y = record.MousePositionY - windowTop;

        if ((record.EventFlags & MouseWheeled) != 0)
        {
            short delta = (short)(record.ButtonState >> 16);
            var button = delta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            return new MouseConsoleInputEvent(x, y, button, MouseEventKind.Wheel, mods);
        }

        uint current = GetSupportedButtonState(record.ButtonState);
        uint previous = _pressedButtons;
        _pressedButtons = current;

        uint pressed = current & ~previous;
        if (pressed != 0)
            return new MouseConsoleInputEvent(x, y, GetPriorityButton(pressed), MouseEventKind.Down, mods);

        if ((record.EventFlags & DoubleClick) != 0 && current != 0)
            return new MouseConsoleInputEvent(x, y, GetPriorityButton(current), MouseEventKind.Down, mods);

        uint released = previous & ~current;
        if (released != 0)
            return new MouseConsoleInputEvent(x, y, GetPriorityButton(released), MouseEventKind.Up, mods);

        if ((record.EventFlags & MouseMoved) != 0 && current != 0)
            return new MouseConsoleInputEvent(x, y, GetPriorityButton(current), MouseEventKind.Move, mods);

        return null;
    }

    public void Reset() => _pressedButtons = 0;

    private static uint GetSupportedButtonState(uint buttonState) =>
        buttonState & (FromLeftFirstButtonPressed | RightmostButtonPressed | FromLeftSecondButtonPressed);

    private static MouseButton GetPriorityButton(uint buttonState)
    {
        if ((buttonState & FromLeftFirstButtonPressed) != 0)
            return MouseButton.Left;
        if ((buttonState & RightmostButtonPressed) != 0)
            return MouseButton.Right;
        if ((buttonState & FromLeftSecondButtonPressed) != 0)
            return MouseButton.Middle;

        throw new ArgumentException("Button state does not contain a supported mouse button.", nameof(buttonState));
    }

    private static MouseKeyModifiers GetModifiers(uint controlKeyState)
    {
        var mods = MouseKeyModifiers.None;
        if ((controlKeyState & ShiftPressed) != 0)
            mods |= MouseKeyModifiers.Shift;
        if ((controlKeyState & (LeftAltPressed | RightAltPressed)) != 0)
            mods |= MouseKeyModifiers.Alt;
        if ((controlKeyState & (LeftCtrlPressed | RightCtrlPressed)) != 0)
            mods |= MouseKeyModifiers.Control;
        return mods;
    }
}
