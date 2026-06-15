using System.ComponentModel;
using System.Runtime.InteropServices;
using CSharpFar.Console.Ansi;

if (OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("TerminalProbe raw mode is Unix-only. Run it inside WSL/Linux.");
    return 1;
}

var options = ProbeOptions.Parse(args);
using var log = options.LogPath is null ? null : new StreamWriter(options.LogPath, append: false) { AutoFlush = true };

return options.Mode switch
{
    "--console" => RunConsoleReadKeyProbe(),
    "--raw" => RunRawProbe(),
    "--help" or "-h" => PrintHelp(),
    _ => PrintHelp()
};

void WriteLine(string text = "")
{
    Console.WriteLine(text);
    log?.WriteLine(text);
}

int PrintHelp()
{
    WriteLine("Usage:");
    WriteLine("  dotnet run --project tools/CSharpFar.TerminalProbe -- --raw [--log <path>]");
    WriteLine("  dotnet run --project tools/CSharpFar.TerminalProbe -- --console [--log <path>]");
    return 0;
}

int RunConsoleReadKeyProbe()
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    WriteLine("Console.ReadKey probe. Press Esc or Ctrl+C to exit.");
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        PrintParsed(key);
        if (IsExit(key))
            return 0;
    }
}

int RunRawProbe()
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    using var terminal = new RawTerminalMode();
    var reader = new RawInputByteReader();
    var parser = new AnsiInputParser();

    WriteLine("Raw stdin probe: cfmakeraw + poll(2) + read(2). Press Esc or Ctrl+C to exit.");
    WriteLine($"TERM={Environment.GetEnvironmentVariable("TERM") ?? ""}");

    while (true)
    {
        var result = parser.Read(reader);
        WriteLine($"bytes: {string.Join(' ', result.Bytes.Select(static b => b.ToString("X2")))}");
        WriteLine($"text : {string.Join(' ', result.Bytes.Select(DescribeByte))}");
        PrintParsed(result.Key);
        WriteLine();

        if (IsExit(result.Key))
            return 0;
    }
}

void PrintParsed(ConsoleKeyInfo key) =>
    WriteLine($"key  : {key.Key}, char=U+{(int)key.KeyChar:X4}, modifiers={key.Modifiers}");

static bool IsExit(ConsoleKeyInfo key) =>
    key.Key == ConsoleKey.Escape ||
    (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control));

static string DescribeByte(byte value) =>
    value switch
    {
        0x1b => "ESC",
        0x09 => "TAB",
        0x0d => "CR",
        0x0a => "LF",
        0x7f => "DEL",
        >= 0x20 and <= 0x7e => ((char)value).ToString(),
        _ => $"0x{value:X2}",
    };

internal sealed record ProbeOptions(string Mode, string? LogPath)
{
    public static ProbeOptions Parse(string[] args)
    {
        string mode = "--raw";
        string? logPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--raw":
                case "--console":
                case "--help":
                case "-h":
                    mode = args[i];
                    break;
                case "--log":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--log requires a path.");
                    logPath = args[++i];
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        return new ProbeOptions(mode, logPath);
    }
}

internal sealed class RawInputByteReader : IAnsiInputByteReader
{
    private const int STDIN_FILENO = 0;
    private const short POLLIN = 0x0001;
    private const int PacketIdleTimeoutMilliseconds = 100;

    private readonly Queue<byte> _pending = new();

    public byte ReadByte()
    {
        while (_pending.Count == 0)
            ReadPacket(block: true);

        return _pending.Dequeue();
    }

    public bool TryReadByte(out byte value)
    {
        if (_pending.Count == 0 && !ReadPacket(block: false))
        {
            value = default;
            return false;
        }

        value = _pending.Dequeue();
        return true;
    }

    public bool WaitForInput(int timeoutMilliseconds)
    {
        if (_pending.Count > 0)
            return true;

        return PollInput(timeoutMilliseconds);
    }

    private bool ReadPacket(bool block)
    {
        if (!PollInput(block ? -1 : 0))
            return false;

        do
        {
            byte[] buffer = new byte[64];
            int count = Posix.ReadInto(buffer);
            if (count < 0)
                throw new InvalidOperationException("read(2) failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

            for (int i = 0; i < count; i++)
                _pending.Enqueue(buffer[i]);
        }
        while (PollInput(PacketIdleTimeoutMilliseconds));

        return _pending.Count > 0;
    }

    private static bool PollInput(int timeoutMilliseconds)
    {
        var fds = new[] { new PollFd { Fd = STDIN_FILENO, Events = POLLIN } };
        int result = Posix.poll(fds, 1, timeoutMilliseconds);
        if (result < 0)
            throw new InvalidOperationException("poll(2) failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

        return result > 0 && (fds[0].Revents & POLLIN) != 0;
    }
}

internal sealed class RawTerminalMode : IDisposable
{
    private const int STDIN_FILENO = 0;
    private const int TCSANOW = 0;
    private const int VTIME = 5;
    private const int VMIN = 6;

    private readonly Termios _original;
    private bool _disposed;

    public RawTerminalMode()
    {
        if (Posix.tcgetattr(STDIN_FILENO, out _original) != 0)
            throw new InvalidOperationException("tcgetattr failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

        var raw = _original;
        Posix.cfmakeraw(ref raw);
        raw.c_cc[VMIN] = 0;
        raw.c_cc[VTIME] = 1;

        if (Posix.tcsetattr(STDIN_FILENO, TCSANOW, ref raw) != 0)
            throw new InvalidOperationException("tcsetattr raw failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var original = _original;
        _ = Posix.tcsetattr(STDIN_FILENO, TCSANOW, ref original);
        _disposed = true;
    }
}

internal static class Posix
{
    private const int STDIN_FILENO = 0;

    public static int ReadInto(byte[] buffer)
    {
        IntPtr nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            nint count = read(STDIN_FILENO, nativeBuffer, (nuint)buffer.Length);
            if (count > 0)
                Marshal.Copy(nativeBuffer, buffer, 0, (int)count);

            return (int)count;
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(PollFd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, IntPtr buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    public static extern int tcgetattr(int fd, out Termios termios);

    [DllImport("libc", SetLastError = true)]
    public static extern int tcsetattr(int fd, int optionalActions, ref Termios termios);

    [DllImport("libc", SetLastError = true)]
    public static extern void cfmakeraw(ref Termios termios);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    public int Fd;
    public short Events;
    public short Revents;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Termios
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
