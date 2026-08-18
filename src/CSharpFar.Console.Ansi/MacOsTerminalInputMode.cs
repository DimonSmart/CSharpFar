using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CSharpFar.Console.Ansi;

internal sealed class MacOsTerminalInputMode : ITerminalInputMode
{
    private const int StdinFileDescriptor = 0;
    private const int Tcsanow = 0;
    private const int Vmin = 16;
    private const int Vtime = 17;

    private readonly Termios _original;
    private readonly Termios _raw;
    private bool _rawActive;
    private bool _disposed;

    public MacOsTerminalInputMode()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("macOS terminal raw mode is not supported on this platform.");

        if (tcgetattr(StdinFileDescriptor, out _original) != 0)
            throw new InvalidOperationException("Failed to read terminal mode.", new Win32Exception(Marshal.GetLastPInvokeError()));

        _raw = _original;
        cfmakeraw(ref _raw);
        _raw.OutputFlags = _original.OutputFlags;
        _raw.ControlCharacters[Vmin] = 0;
        _raw.ControlCharacters[Vtime] = 1;
        EnableRawMode();
    }

    public void EnableRawMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rawActive)
            return;

        var raw = _raw;
        if (tcsetattr(StdinFileDescriptor, Tcsanow, ref raw) != 0)
            throw new InvalidOperationException("Failed to enable terminal raw mode.", new Win32Exception(Marshal.GetLastPInvokeError()));
        _rawActive = true;
    }

    public void RestoreOriginalMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_rawActive)
            return;

        var original = _original;
        if (tcsetattr(StdinFileDescriptor, Tcsanow, ref original) != 0)
            throw new InvalidOperationException("Failed to restore terminal mode.", new Win32Exception(Marshal.GetLastPInvokeError()));
        _rawActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        var original = _original;
        _ = tcsetattr(StdinFileDescriptor, Tcsanow, ref original);
        _rawActive = false;
        _disposed = true;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fileDescriptor, out Termios terminalAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fileDescriptor, int optionalActions, ref Termios terminalAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern void cfmakeraw(ref Termios terminalAttributes);

    // Darwin termios: four unsigned-long flags, NCCS=20, then two speed_t values.
    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public ulong InputFlags;
        public ulong OutputFlags;
        public ulong ControlFlags;
        public ulong LocalFlags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] ControlCharacters;
        public ulong InputSpeed;
        public ulong OutputSpeed;
    }
}
