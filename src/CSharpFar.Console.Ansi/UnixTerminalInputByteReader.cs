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
    private const int MaxReadsPerBatch = 8;

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

        return PollForInput(timeoutMilliseconds);
    }

    private bool ReadPacket(bool block)
    {
        if (!PollForInput(block ? -1 : 0))
            return false;

        byte[] buffer = new byte[256];
        // Drain only bytes that are already available. Waiting for a quiet
        // period adds latency to every keypress and can starve continuous input.
        for (int i = 0; i < MaxReadsPerBatch; i++)
        {
            int readCount = ReadInto(buffer);
            if (readCount == -InterruptedSystemCall)
                continue;
            if (readCount < 0)
                throw new InvalidOperationException("Failed to read terminal input.", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (readCount == 0)
            {
                if (_pending.Count == 0)
                    throw new EndOfStreamException("Terminal input closed.");
                break;
            }

            for (int j = 0; j < readCount; j++)
                _pending.Enqueue(buffer[j]);

            if (!PollForInput(0))
                break;
        }

        return _pending.Count > 0;
    }

    private static bool PollForInput(int timeoutMilliseconds)
    {
        while (true)
        {
            var fds = new[] { new PollFd { Fd = StdinFileDescriptor, Events = PollInput } };
            int result = poll(fds, 1, timeoutMilliseconds);
            if (result >= 0)
                return result > 0 && (fds[0].Revents & (PollInput | PollError | PollHangup)) != 0;

            int error = Marshal.GetLastPInvokeError();
            if (error == InterruptedSystemCall)
                continue;

            throw new InvalidOperationException("Failed to poll terminal input.", new Win32Exception(error));
        }
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
}
