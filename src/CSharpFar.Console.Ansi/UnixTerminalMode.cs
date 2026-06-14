using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CSharpFar.Console.Ansi;

internal sealed class UnixTerminalMode : IDisposable
{
    private const int STDIN_FILENO = 0;
    private const uint BRKINT = 0x0002;
    private const uint ICRNL = 0x0100;
    private const uint INPCK = 0x0010;
    private const uint ISTRIP = 0x0020;
    private const uint IXON = 0x0400;
    private const uint OPOST = 0x0001;
    private const uint ECHO = 0x0008;
    private const uint ICANON = 0x0002;
    private const uint IEXTEN = 0x8000;
    private const uint ISIG = 0x0001;
    private const uint CS8 = 0x0030;
    private const int VMIN = 6;
    private const int VTIME = 5;
    private const int TCSANOW = 0;

    private readonly Termios _original;
    private readonly Termios _raw;
    private bool _rawActive;
    private bool _disposed;

    public UnixTerminalMode()
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Unix terminal raw mode is not supported on Windows.");

        if (tcgetattr(STDIN_FILENO, out _original) != 0)
            throw new InvalidOperationException("Failed to read terminal mode.", new Win32Exception(Marshal.GetLastPInvokeError()));

        _raw = _original;
        _raw.c_iflag &= ~(BRKINT | ICRNL | INPCK | ISTRIP | IXON);
        _raw.c_oflag &= ~OPOST;
        _raw.c_cflag |= CS8;
        _raw.c_lflag &= ~(ECHO | ICANON | IEXTEN | ISIG);
        _raw.c_cc[VMIN] = 1;
        _raw.c_cc[VTIME] = 0;

        EnableRawMode();
    }

    public void EnableRawMode()
    {
        ThrowIfDisposed();
        if (_rawActive)
            return;

        var raw = _raw;
        if (tcsetattr(STDIN_FILENO, TCSANOW, ref raw) != 0)
            throw new InvalidOperationException("Failed to enable terminal raw mode.", new Win32Exception(Marshal.GetLastPInvokeError()));

        _rawActive = true;
    }

    public void RestoreOriginalMode()
    {
        ThrowIfDisposed();
        if (!_rawActive)
            return;

        var original = _original;
        if (tcsetattr(STDIN_FILENO, TCSANOW, ref original) != 0)
            throw new InvalidOperationException("Failed to restore terminal mode.", new Win32Exception(Marshal.GetLastPInvokeError()));

        _rawActive = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var original = _original;
        _ = tcsetattr(STDIN_FILENO, TCSANOW, ref original);
        _rawActive = false;
        _disposed = true;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, out Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, ref Termios termios);

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public uint c_iflag;
        public uint c_oflag;
        public uint c_cflag;
        public uint c_lflag;
        public byte c_line;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] c_cc;

        public uint c_ispeed;
        public uint c_ospeed;
    }
}
