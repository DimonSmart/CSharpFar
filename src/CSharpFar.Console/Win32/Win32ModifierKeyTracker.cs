using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Win32;

[SupportedOSPlatform("windows")]
internal sealed class Win32ModifierKeyTracker : IModifierKeyTracker
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_KEYUP       = 0x0101;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_SYSKEYUP    = 0x0105;
    private const int WM_QUIT        = 0x0012;

    private const int VK_SHIFT    = 0x10;
    private const int VK_CONTROL  = 0x11;
    private const int VK_MENU     = 0x12;
    private const int VK_LSHIFT   = 0xA0;
    private const int VK_RSHIFT   = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU    = 0xA4;
    private const int VK_RMENU    = 0xA5;

    private readonly LowLevelKeyboardProc _hookProc;
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;

    private IntPtr _hookHandle;
    private int _hookThreadId;
    private int _shiftDown;
    private int _controlDown;
    private int _altDown;
    private ConsoleModifiers _lastModifiers;
    private bool _disposed;

    public Win32ModifierKeyTracker()
    {
        _hookProc = HookCallback;
        _thread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "CSharpFar modifier-key hook",
        };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(1));
    }

    public string BackendName => "win32-low-level-hook";

    public ModifierKeyTrackingSnapshot GetSnapshot() =>
        new(
            BackendName,
            IsPlatformSupported: OperatingSystem.IsWindows(),
            IsEnabled: !_disposed && _hookHandle != IntPtr.Zero,
            CanTrackShiftOnly: !_disposed && _hookHandle != IntPtr.Zero,
            Status: OperatingSystem.IsWindows()
                ? (_hookHandle != IntPtr.Zero ? ModifierKeyTrackingStatus.Enabled : ModifierKeyTrackingStatus.Failed)
                : ModifierKeyTrackingStatus.PlatformNotSupported,
            FailureReason: OperatingSystem.IsWindows() && _hookHandle == IntPtr.Zero
                ? "Low-level keyboard hook was not installed."
                : null,
            Devices: []);

    public bool TryCreateInputEvent([NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent) =>
        TryCreateModifierStateChangeEvent(
            ref _lastModifiers,
            GetCurrentModifiers(),
            out inputEvent);

    public void ObserveConsoleInput(ConsoleInputEvent inputEvent)
    {
        var modifiers = inputEvent switch
        {
            ModifierKeyConsoleInputEvent modifier => modifier.Modifiers,
            KeyConsoleInputEvent key => key.Key.Modifiers,
            _ => (ConsoleModifiers?)null,
        };

        if (!modifiers.HasValue)
            return;

        _lastModifiers = modifiers.Value;
        UpdateHookModifiers(modifiers.Value);
    }

    public void Suspend()
    {
    }

    public void Resume()
    {
    }

    internal static bool TryCreateModifierStateChangeEvent(
        ref ConsoleModifiers lastModifiers,
        ConsoleModifiers currentModifiers,
        [NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
    {
        if (currentModifiers == lastModifiers)
        {
            inputEvent = null;
            return false;
        }

        lastModifiers = currentModifiers;
        inputEvent = new ModifierKeyConsoleInputEvent(currentModifiers);
        return true;
    }

    private ConsoleModifiers GetCurrentModifiers() =>
        GetPhysicalModifiers() | GetHookModifiers();

    private ConsoleModifiers GetHookModifiers()
    {
        var modifiers = default(ConsoleModifiers);

        if (Volatile.Read(ref _shiftDown) != 0)
            modifiers |= ConsoleModifiers.Shift;
        if (Volatile.Read(ref _controlDown) != 0)
            modifiers |= ConsoleModifiers.Control;
        if (Volatile.Read(ref _altDown) != 0)
            modifiers |= ConsoleModifiers.Alt;

        return modifiers;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();

        using var process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        IntPtr moduleHandle = module?.ModuleName is { } moduleName
            ? GetModuleHandle(moduleName)
            : IntPtr.Zero;

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, moduleHandle, 0);
        _started.Set();

        if (_hookHandle == IntPtr.Zero)
            return;

        try
        {
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        finally
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            bool? isDown = message switch
            {
                WM_KEYDOWN or WM_SYSKEYDOWN => true,
                WM_KEYUP or WM_SYSKEYUP => false,
                _ => null,
            };

            if (isDown.HasValue)
            {
                var key = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                UpdateModifierKeyState((int)key.VkCode, isDown.Value);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void UpdateModifierKeyState(int virtualKeyCode, bool isDown)
    {
        int value = isDown ? 1 : 0;

        switch (virtualKeyCode)
        {
            case VK_SHIFT:
            case VK_LSHIFT:
            case VK_RSHIFT:
                Volatile.Write(ref _shiftDown, value);
                break;
            case VK_CONTROL:
            case VK_LCONTROL:
            case VK_RCONTROL:
                Volatile.Write(ref _controlDown, value);
                break;
            case VK_MENU:
            case VK_LMENU:
            case VK_RMENU:
                Volatile.Write(ref _altDown, value);
                break;
            default:
                return;
        }
    }

    private void UpdateHookModifiers(ConsoleModifiers modifiers)
    {
        Volatile.Write(ref _shiftDown, HasModifier(modifiers, ConsoleModifiers.Shift) ? 1 : 0);
        Volatile.Write(ref _controlDown, HasModifier(modifiers, ConsoleModifiers.Control) ? 1 : 0);
        Volatile.Write(ref _altDown, HasModifier(modifiers, ConsoleModifiers.Alt) ? 1 : 0);
    }

    private static ConsoleModifiers GetPhysicalModifiers()
    {
        var modifiers = default(ConsoleModifiers);
        bool rightAltDown = IsKeyDown(VK_RMENU);

        if (IsKeyDown(VK_SHIFT) || IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT))
            modifiers |= ConsoleModifiers.Shift;
        if (IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || rightAltDown)
            modifiers |= ConsoleModifiers.Alt;
        if (IsKeyDown(VK_RCONTROL) || (!rightAltDown && (IsKeyDown(VK_CONTROL) || IsKeyDown(VK_LCONTROL))))
            modifiers |= ConsoleModifiers.Control;

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKeyCode) =>
        (GetAsyncKeyState(virtualKeyCode) & unchecked((short)0x8000)) != 0;

    private static bool HasModifier(ConsoleModifiers modifiers, ConsoleModifiers modifier) =>
        (modifiers & modifier) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(
        int idThread,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(
        out Msg lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }
}
