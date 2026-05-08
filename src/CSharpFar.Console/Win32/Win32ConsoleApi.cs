using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Win32;

[SupportedOSPlatform("windows")]
internal static class Win32ConsoleApi
{
    public const uint ENABLE_PROCESSED_INPUT    = 0x0001;
    public const uint ENABLE_MOUSE_INPUT        = 0x0010;
    public const uint ENABLE_INSERT_MODE        = 0x0020;
    public const uint ENABLE_QUICK_EDIT_MODE    = 0x0040;
    public const uint ENABLE_EXTENDED_FLAGS     = 0x0080;
    public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
    public const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;

    private const ushort KEY_EVENT               = 0x0001;
    private const ushort MOUSE_EVENT             = 0x0002;
    private const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;
    private const uint RIGHT_ALT_PRESSED  = 0x0001;
    private const uint LEFT_ALT_PRESSED   = 0x0002;
    private const uint RIGHT_CTRL_PRESSED = 0x0004;
    private const uint LEFT_CTRL_PRESSED  = 0x0008;
    private const uint SHIFT_PRESSED      = 0x0010;

    private const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
    private const uint RIGHTMOST_BUTTON_PRESSED     = 0x0002;
    private const uint FROM_LEFT_2ND_BUTTON_PRESSED = 0x0004;
    private const uint MOUSE_MOVED   = 0x0001;
    private const uint DOUBLE_CLICK  = 0x0002;
    private const uint MOUSE_WHEELED = 0x0004;

    private const uint WAIT_TIMEOUT   = 0x00000102;
    private const uint WAIT_OBJECT_0  = 0x00000000;

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const char EscapeChar = '\x1b';
    private const uint VirtualTerminalKeyTimeoutMilliseconds = 150;
    private const int VirtualTerminalKeyMaxLength = 16;

    private static readonly object s_pendingLock = new();
    private static readonly Queue<InputRecord> s_pendingRecords = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

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

    /// <summary>
    /// Reads the next input event, blocking up to 250 ms at a time so that
    /// a <see cref="CancellationToken"/> can interrupt the wait.
    /// Skips events that produce no logical event (mouse moves, key-ups, etc.).
    /// </summary>
    public static ConsoleInputEvent ReadInput(
        IntPtr inputHandle,
        bool intercept,
        CancellationToken cancellationToken,
        Func<bool>? hasVisibleWindowSizeChanged = null)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (HasPendingRecord())
            {
                var pendingEvt = TryReadInputRecord(inputHandle, intercept);
                if (pendingEvt != null)
                    return pendingEvt;
                if (hasVisibleWindowSizeChanged?.Invoke() == true)
                    return new ConsoleResizeInputEvent();
                continue;
            }

            uint result = WaitForSingleObject(inputHandle, 250);

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (result == WAIT_TIMEOUT)
            {
                if (hasVisibleWindowSizeChanged?.Invoke() == true)
                    return new ConsoleResizeInputEvent();
                continue;
            }

            // Input event available
            var evt = TryReadInputRecord(inputHandle, intercept);
            if (evt != null)
                return evt;
            if (hasVisibleWindowSizeChanged?.Invoke() == true)
                return new ConsoleResizeInputEvent();
            // null = irrelevant event (move, key-up) – loop again
        }
    }

    private static ConsoleInputEvent? TryReadInputRecord(IntPtr inputHandle, bool intercept)
    {
        if (!TryReadNextRecord(inputHandle, out var record))
            return null;

        if (record.EventType == WINDOW_BUFFER_SIZE_EVENT)
            return new ConsoleResizeInputEvent();

        if (record.EventType == MOUSE_EVENT)
            return ParseMouseEvent(record.MouseEvent);

        if (record.EventType == KEY_EVENT && record.KeyEvent.IsKeyDown)
        {
            var modifiers = GetModifiers(record.KeyEvent.ControlKeyState);
            var key = Enum.IsDefined(typeof(ConsoleKey), (int)record.KeyEvent.VirtualKeyCode)
                ? (ConsoleKey)record.KeyEvent.VirtualKeyCode
                : ConsoleKey.NoName;
            char unicodeChar = record.KeyEvent.Character;

            if (unicodeChar == EscapeChar &&
                TryParseVirtualTerminalKey(inputHandle, out var vtKey))
            {
                return new KeyConsoleInputEvent(vtKey);
            }

            char keyChar = IsNonTextKey(key)
                ? '\0'
                : unicodeChar;

            var keyInfo = new ConsoleKeyInfo(
                keyChar,
                key,
                (modifiers & ConsoleModifiers.Shift)   != 0,
                (modifiers & ConsoleModifiers.Alt)     != 0,
                (modifiers & ConsoleModifiers.Control) != 0);

            if (!intercept && keyChar != '\0')
                global::System.Console.Write(keyChar);

            return new KeyConsoleInputEvent(keyInfo);
        }

        return null;
    }

    private static ConsoleInputEvent? ParseMouseEvent(MouseEventRecord rec)
    {
        var mods = MouseKeyModifiers.None;
        if ((rec.ControlKeyState & SHIFT_PRESSED) != 0) mods |= MouseKeyModifiers.Shift;
        if ((rec.ControlKeyState & (LEFT_ALT_PRESSED | RIGHT_ALT_PRESSED)) != 0) mods |= MouseKeyModifiers.Alt;
        if ((rec.ControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0) mods |= MouseKeyModifiers.Control;

        // Normalize to visible window coordinates
        int x = rec.MousePositionX - global::System.Console.WindowLeft;
        int y = rec.MousePositionY - global::System.Console.WindowTop;

        if ((rec.EventFlags & MOUSE_WHEELED) != 0)
        {
            short delta = (short)(rec.ButtonState >> 16);
            var btn = delta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
            return new MouseConsoleInputEvent(x, y, btn, MouseEventKind.Wheel, mods);
        }

        if ((rec.EventFlags & DOUBLE_CLICK) != 0)
        {
            var btn = GetMouseButton(rec.ButtonState);
            return btn.HasValue
                ? new MouseConsoleInputEvent(x, y, btn.Value, MouseEventKind.DoubleClick, mods)
                : null;
        }

        if ((rec.EventFlags & MOUSE_MOVED) != 0)
            return null; // skip move events

        // Button down/up
        var button = GetMouseButton(rec.ButtonState);
        return button.HasValue
            ? new MouseConsoleInputEvent(x, y, button.Value, MouseEventKind.Down, mods)
            : null;
    }

    private static MouseButton? GetMouseButton(uint buttonState) =>
        (buttonState & FROM_LEFT_1ST_BUTTON_PRESSED) != 0 ? MouseButton.Left  :
        (buttonState & RIGHTMOST_BUTTON_PRESSED)     != 0 ? MouseButton.Right :
        (buttonState & FROM_LEFT_2ND_BUTTON_PRESSED) != 0 ? MouseButton.Middle :
        null;

    public static bool TryReadKey(IntPtr inputHandle, bool intercept, out ConsoleKeyInfo keyInfo)
    {
        while (TryReadNextRecord(inputHandle, out var record))
        {
            if (record.EventType == WINDOW_BUFFER_SIZE_EVENT)
            {
                keyInfo = new ConsoleKeyInfo('\0', ConsoleKey.NoName, shift: false, alt: false, control: false);
                return true;
            }

            if (record.EventType != KEY_EVENT || !record.KeyEvent.IsKeyDown)
                continue;

            var modifiers = GetModifiers(record.KeyEvent.ControlKeyState);
            var key = Enum.IsDefined(typeof(ConsoleKey), (int)record.KeyEvent.VirtualKeyCode)
                ? (ConsoleKey)record.KeyEvent.VirtualKeyCode
                : ConsoleKey.NoName;
            char unicodeChar = record.KeyEvent.Character;

            if (unicodeChar == EscapeChar &&
                TryParseVirtualTerminalKey(inputHandle, out keyInfo))
            {
                return true;
            }

            char keyChar = IsNonTextKey(key)
                ? '\0'
                : unicodeChar;

            keyInfo = new ConsoleKeyInfo(
                keyChar,
                key,
                (modifiers & ConsoleModifiers.Shift) != 0,
                (modifiers & ConsoleModifiers.Alt) != 0,
                (modifiers & ConsoleModifiers.Control) != 0);

            if (!intercept && keyChar != '\0')
                global::System.Console.Write(keyChar);

            return true;
        }

        keyInfo = default;
        return false;
    }

    private static bool TryReadNextRecord(IntPtr inputHandle, out InputRecord record)
    {
        if (TryDequeuePendingRecord(out record))
            return true;

        var buffer = new InputRecord[1];
        if (!ReadConsoleInput(inputHandle, buffer, 1, out uint read) || read == 0)
        {
            record = default;
            return false;
        }

        record = buffer[0];
        return true;
    }

    private static bool TryReadNextRecordWithTimeout(
        IntPtr inputHandle,
        uint timeoutMilliseconds,
        out InputRecord record)
    {
        if (TryDequeuePendingRecord(out record))
            return true;

        if (WaitForSingleObject(inputHandle, timeoutMilliseconds) != WAIT_OBJECT_0)
        {
            record = default;
            return false;
        }

        var buffer = new InputRecord[1];
        if (!ReadConsoleInput(inputHandle, buffer, 1, out uint read) || read == 0)
        {
            record = default;
            return false;
        }

        record = buffer[0];
        return true;
    }

    private static bool HasPendingRecord()
    {
        lock (s_pendingLock)
            return s_pendingRecords.Count > 0;
    }

    private static bool TryDequeuePendingRecord(out InputRecord record)
    {
        lock (s_pendingLock)
        {
            if (s_pendingRecords.Count > 0)
            {
                record = s_pendingRecords.Dequeue();
                return true;
            }
        }

        record = default;
        return false;
    }

    private static void EnqueuePendingRecords(IEnumerable<InputRecord> records)
    {
        lock (s_pendingLock)
        {
            foreach (var record in records)
                s_pendingRecords.Enqueue(record);
        }
    }

    private static bool TryReadKeyDownCharWithTimeout(
        IntPtr inputHandle,
        uint timeoutMilliseconds,
        List<InputRecord> rollbackRecords,
        List<InputRecord> deferredRecords,
        out char ch,
        out InputRecord keyDownRecord)
    {
        while (TryReadNextRecordWithTimeout(inputHandle, timeoutMilliseconds, out var record))
        {
            rollbackRecords.Add(record);

            if (record.EventType == KEY_EVENT)
            {
                if (!record.KeyEvent.IsKeyDown)
                    continue;

                ch = record.KeyEvent.Character;
                keyDownRecord = record;
                return true;
            }

            if (record.EventType is MOUSE_EVENT or WINDOW_BUFFER_SIZE_EVENT)
            {
                deferredRecords.Add(record);
                continue;
            }
        }

        ch = '\0';
        keyDownRecord = default;
        return false;
    }

    private static bool TryParseVirtualTerminalKey(
        IntPtr inputHandle,
        out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;
        var rollbackRecords = new List<InputRecord>();
        var deferredRecords = new List<InputRecord>();

        if (!TryReadKeyDownCharWithTimeout(
                inputHandle,
                VirtualTerminalKeyTimeoutMilliseconds,
                rollbackRecords,
                deferredRecords,
                out char prefix,
                out _))
        {
            return false;
        }

        if (prefix != '[' && prefix != 'O')
        {
            EnqueuePendingRecords(rollbackRecords);
            return false;
        }

        var sequence = new List<char>();

        for (int i = 0; i < VirtualTerminalKeyMaxLength; i++)
        {
            if (!TryReadKeyDownCharWithTimeout(
                    inputHandle,
                    VirtualTerminalKeyTimeoutMilliseconds,
                    rollbackRecords,
                    deferredRecords,
                    out char ch,
                    out _))
            {
                EnqueuePendingRecords(rollbackRecords);
                return false;
            }

            sequence.Add(ch);

            if (VirtualTerminalKeyParser.IsFinalChar(ch))
                break;
        }

        if (sequence.Count == 0 || !VirtualTerminalKeyParser.IsFinalChar(sequence[^1]))
        {
            EnqueuePendingRecords(rollbackRecords);
            return false;
        }

        bool parsed = VirtualTerminalKeyParser.TryParse(prefix, sequence, out keyInfo);

        if (!parsed)
        {
            EnqueuePendingRecords(rollbackRecords);
            return false;
        }

        EnqueuePendingRecords(deferredRecords);
        return true;
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

    private static bool IsNonTextKey(ConsoleKey key) =>
        key is >= ConsoleKey.F1 and <= ConsoleKey.F24
            or ConsoleKey.UpArrow
            or ConsoleKey.DownArrow
            or ConsoleKey.LeftArrow
            or ConsoleKey.RightArrow
            or ConsoleKey.Home
            or ConsoleKey.End
            or ConsoleKey.PageUp
            or ConsoleKey.PageDown
            or ConsoleKey.Insert
            or ConsoleKey.Delete;

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
    [FieldOffset(4)] public KeyEventRecord  KeyEvent;
    [FieldOffset(4)] public MouseEventRecord MouseEvent;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseEventRecord
{
    public short MousePositionX;
    public short MousePositionY;
    public uint  ButtonState;
    public uint  ControlKeyState;
    public uint  EventFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyEventRecord
{
    public int KeyDown;
    public ushort RepeatCount;
    public ushort VirtualKeyCode;
    public ushort VirtualScanCode;
    public ushort UnicodeChar;
    public uint ControlKeyState;

    public bool IsKeyDown => KeyDown != 0;
    public char Character => (char)UnicodeChar;
}
