using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CSharpFar.Console.Ansi;

internal sealed class UnixTerminalInputByteReader : IAnsiInputByteReader
{
    private const int StdinFileDescriptor = 0;
    private const int InterruptedSystemCall = 4;
    private const short PollInput = 0x0001;
    private const short PollError = 0x0008;
    private const short PollHangup = 0x0010;
    private const short PollInvalid = 0x0020;
    private const int MaxReadsPerBatch = 8;

    private readonly Queue<byte> _pending = new();
    private readonly Func<int, PollResult> _pollForInput;
    private readonly Func<byte[], int> _readInto;

    public UnixTerminalInputByteReader()
        : this(PollForInput, ReadInto)
    {
    }

    internal UnixTerminalInputByteReader(
        Func<int, PollResult> pollForInput,
        Func<byte[], int> readInto)
    {
        _pollForInput = pollForInput ?? throw new ArgumentNullException(nameof(pollForInput));
        _readInto = readInto ?? throw new ArgumentNullException(nameof(readInto));
    }

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

        PollResult result = _pollForInput(timeoutMilliseconds);
        ThrowIfPollFailed(result);
        return result.IsReady;
    }

    private bool ReadPacket(bool block)
    {
        PollResult pollResult = _pollForInput(block ? -1 : 0);
        if (!pollResult.IsReady)
            return false;
        ThrowIfPollFailed(pollResult);

        byte[] buffer = new byte[256];
        // Drain only bytes that are already available. Waiting for a quiet
        // period adds latency to every keypress and can starve continuous input.
        for (int i = 0; i < MaxReadsPerBatch; i++)
        {
            int readCount = _readInto(buffer);
            if (readCount == -InterruptedSystemCall)
                continue;
            if (readCount < 0)
                throw new InvalidOperationException("Failed to read terminal input.", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (readCount == 0)
            {
                if (_pending.Count == 0 && pollResult.HasHangup)
                    throw new EndOfStreamException("Terminal input closed.");
                break;
            }

            for (int j = 0; j < readCount; j++)
                _pending.Enqueue(buffer[j]);

            pollResult = _pollForInput(0);
            if (!pollResult.HasInput)
                break;
            ThrowIfPollFailed(pollResult);
        }

        return _pending.Count > 0;
    }

    private static PollResult PollForInput(int timeoutMilliseconds)
    {
        while (true)
        {
            var fds = new[] { new PollFd { Fd = StdinFileDescriptor, Events = PollInput } };
            int result = poll(fds, 1, timeoutMilliseconds);
            if (result == 0)
                return PollResult.None;
            if (result > 0)
            {
                short events = fds[0].Revents;
                return new PollResult(
                    HasInput: (events & PollInput) != 0,
                    HasHangup: (events & PollHangup) != 0,
                    HasError: (events & PollError) != 0,
                    IsInvalid: (events & PollInvalid) != 0);
            }

            int error = Marshal.GetLastPInvokeError();
            if (error == InterruptedSystemCall)
                continue;

            throw new InvalidOperationException("Failed to poll terminal input.", new Win32Exception(error));
        }
    }

    private static void ThrowIfPollFailed(PollResult result)
    {
        if (result.IsInvalid)
            throw new InvalidOperationException("Terminal input descriptor is invalid.");
        if (result.HasError)
            throw new InvalidOperationException("Terminal input poll reported an I/O error.");
    }

    private static int ReadInto(byte[] buffer)
    {
        IntPtr nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            nint readCount = read(StdinFileDescriptor, nativeBuffer, (nuint)buffer.Length);
            if (readCount < 0 && Marshal.GetLastPInvokeError() == InterruptedSystemCall)
                return -InterruptedSystemCall;
            if (readCount > 0)
                Marshal.Copy(nativeBuffer, buffer, 0, (int)readCount);

            return (int)readCount;
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, IntPtr buffer, nuint count);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    internal readonly record struct PollResult(
        bool HasInput,
        bool HasHangup = false,
        bool HasError = false,
        bool IsInvalid = false)
    {
        public bool IsReady => HasInput || HasHangup || HasError || IsInvalid;

        public static PollResult None => default;
        public static PollResult Input => new(HasInput: true);
        public static PollResult Hangup => new(HasInput: false, HasHangup: true);
    }
}
