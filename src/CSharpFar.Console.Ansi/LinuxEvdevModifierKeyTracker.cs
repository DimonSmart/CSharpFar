using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using CSharpFar.Console.Input;

namespace CSharpFar.Console.Ansi;

internal sealed class LinuxEvdevModifierKeyTracker : IModifierKeyTracker
{
    private const string InputDeviceDirectory = "/dev/input";
    private const string EventDevicePattern = "event*";
    private const int O_RDONLY = 0;
    private const int O_NONBLOCK = 0x800;
    private const short POLLIN = 0x0001;
    private const ushort EV_KEY = 0x01;
    private const ushort KEY_LEFTSHIFT = 42;
    private const ushort KEY_RIGHTSHIFT = 54;
    private const int EACCES = 13;
    private const int EPERM = 1;

    private readonly List<DeviceHandle> _devices;
    private readonly ModifierKeyTrackingSnapshot _snapshot;
    private readonly Thread? _thread;
    private readonly object _eventLock = new();
    private ConsoleModifiers _lastEmittedModifiers;
    private int _leftShiftDown;
    private int _rightShiftDown;
    private int _suspended;
    private bool _disposed;

    private LinuxEvdevModifierKeyTracker(
        List<DeviceHandle> devices,
        ModifierKeyTrackingSnapshot snapshot)
    {
        _devices = devices;
        _snapshot = snapshot;

        if (_devices.Count == 0)
            return;

        _thread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "CSharpFar linux evdev modifier tracker",
        };
        _thread.Start();
    }

    public string BackendName => "linux-evdev";

    public static LinuxEvdevModifierKeyTracker CreateFailed(string failureReason) =>
        new(
            [],
            new ModifierKeyTrackingSnapshot(
                "linux-evdev",
                IsPlatformSupported: OperatingSystem.IsLinux(),
                IsEnabled: false,
                CanTrackShiftOnly: false,
                Status: ModifierKeyTrackingStatus.Failed,
                FailureReason: failureReason,
                Devices: []));

    public static LinuxEvdevModifierKeyTracker Create()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new LinuxEvdevModifierKeyTracker(
                [],
                new ModifierKeyTrackingSnapshot(
                    "linux-evdev",
                    IsPlatformSupported: false,
                    IsEnabled: false,
                    CanTrackShiftOnly: false,
                    Status: ModifierKeyTrackingStatus.PlatformNotSupported,
                    FailureReason: null,
                    Devices: []));
        }

        if (!Directory.Exists(InputDeviceDirectory))
        {
            return new LinuxEvdevModifierKeyTracker(
                [],
                new ModifierKeyTrackingSnapshot(
                    "linux-evdev",
                    IsPlatformSupported: true,
                    IsEnabled: false,
                    CanTrackShiftOnly: false,
                    Status: ModifierKeyTrackingStatus.NoInputDeviceDirectory,
                    FailureReason: $"{InputDeviceDirectory} does not exist.",
                    Devices: []));
        }

        var handles = new List<DeviceHandle>();
        var snapshots = new List<ModifierKeyDeviceSnapshot>();
        string? firstPermissionDenied = null;
        string? firstFailure = null;

        foreach (string path in Directory.EnumerateFiles(InputDeviceDirectory, EventDevicePattern)
                     .Order(StringComparer.Ordinal))
        {
            int fd = open(path, O_RDONLY | O_NONBLOCK);
            if (fd < 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                string error = DescribeErrno(errno);
                if (errno is EACCES or EPERM)
                    firstPermissionDenied ??= $"{path}: {error}";
                else
                    firstFailure ??= $"{path}: {error}";

                snapshots.Add(new ModifierKeyDeviceSnapshot(
                    path,
                    Name: null,
                    IsReadable: false,
                    HasShiftCapability: false,
                    Error: error));
                continue;
            }

            string? name = TryReadDeviceName(fd);
            bool hasShift = TryHasShiftCapability(fd, out string? capabilityError);
            snapshots.Add(new ModifierKeyDeviceSnapshot(
                path,
                name,
                IsReadable: true,
                HasShiftCapability: hasShift,
                Error: capabilityError));

            if (hasShift)
            {
                handles.Add(new DeviceHandle(path, fd));
                continue;
            }

            close(fd);
        }

        int readableCount = snapshots.Count(static device => device.IsReadable);
        int shiftCapableCount = snapshots.Count(static device => device.IsReadable && device.HasShiftCapability);
        string status;
        string? failureReason;

        if (handles.Count > 0)
        {
            status = ModifierKeyTrackingStatus.Enabled;
            failureReason = null;
        }
        else if (snapshots.Count == 0)
        {
            status = ModifierKeyTrackingStatus.NoReadableDevices;
            failureReason = "No /dev/input/event* devices were found.";
        }
        else if (readableCount == 0 && firstPermissionDenied is not null)
        {
            status = ModifierKeyTrackingStatus.PermissionDenied;
            failureReason = firstPermissionDenied;
        }
        else if (readableCount == 0)
        {
            status = ModifierKeyTrackingStatus.NoReadableDevices;
            failureReason = firstFailure ?? "No readable /dev/input/event* devices were found.";
        }
        else
        {
            status = ModifierKeyTrackingStatus.NoShiftCapableDevices;
            failureReason = "No readable evdev device reported left or right Shift capability.";
        }

        var snapshot = new ModifierKeyTrackingSnapshot(
            "linux-evdev",
            IsPlatformSupported: true,
            IsEnabled: handles.Count > 0,
            CanTrackShiftOnly: handles.Count > 0,
            Status: status,
            FailureReason: failureReason,
            Devices: snapshots);

        return new LinuxEvdevModifierKeyTracker(handles, snapshot);
    }

    public ModifierKeyTrackingSnapshot GetSnapshot() => _snapshot;

    public bool TryCreateInputEvent([NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
    {
        if (Volatile.Read(ref _suspended) != 0)
        {
            inputEvent = null;
            return false;
        }

        var current = GetCurrentModifiers();
        lock (_eventLock)
        {
            if (current == _lastEmittedModifiers)
            {
                inputEvent = null;
                return false;
            }

            _lastEmittedModifiers = current;
        }

        inputEvent = new ModifierKeyConsoleInputEvent(current);
        return true;
    }

    public void ObserveConsoleInput(ConsoleInputEvent inputEvent)
    {
        if (inputEvent is ModifierKeyConsoleInputEvent modifier)
        {
            lock (_eventLock)
                _lastEmittedModifiers = modifier.Modifiers;
        }
    }

    public void Suspend()
    {
        Volatile.Write(ref _suspended, 1);
    }

    public void Resume()
    {
        Volatile.Write(ref _suspended, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _thread?.Join(TimeSpan.FromMilliseconds(200));
        foreach (var device in _devices)
            close(device.FileDescriptor);
        _devices.Clear();
    }

    internal void SetShiftStateForTests(bool leftShiftDown, bool rightShiftDown)
    {
        Volatile.Write(ref _leftShiftDown, leftShiftDown ? 1 : 0);
        Volatile.Write(ref _rightShiftDown, rightShiftDown ? 1 : 0);
    }

    internal static bool TryCreateModifierStateChangeEvent(
        ref ConsoleModifiers lastModifiers,
        bool leftShiftDown,
        bool rightShiftDown,
        [NotNullWhen(true)] out ModifierKeyConsoleInputEvent? inputEvent)
    {
        var current = leftShiftDown || rightShiftDown
            ? ConsoleModifiers.Shift
            : default;

        if (current == lastModifiers)
        {
            inputEvent = null;
            return false;
        }

        lastModifiers = current;
        inputEvent = new ModifierKeyConsoleInputEvent(current);
        return true;
    }

    private void ReadLoop()
    {
        var pollFds = _devices
            .Select(static device => new PollFd(device.FileDescriptor, POLLIN, 0))
            .ToArray();
        byte[] buffer = new byte[InputEventSize * 16];

        while (!_disposed)
        {
            int ready = poll(pollFds, (nuint)pollFds.Length, 100);
            if (ready <= 0)
                continue;

            for (int i = 0; i < pollFds.Length; i++)
            {
                if ((pollFds[i].Revents & POLLIN) == 0)
                    continue;

                ReadAvailableEvents(pollFds[i].Fd, buffer);
            }
        }
    }

    private void ReadAvailableEvents(int fd, byte[] buffer)
    {
        while (!_disposed)
        {
            nint bytesRead = read(fd, buffer, (nuint)buffer.Length);
            if (bytesRead <= 0)
                return;

            int completeEvents = (int)bytesRead / InputEventSize;
            for (int i = 0; i < completeEvents; i++)
                HandleInputEvent(buffer.AsSpan(i * InputEventSize, InputEventSize));

            if (bytesRead < buffer.Length)
                return;
        }
    }

    private void HandleInputEvent(ReadOnlySpan<byte> inputEvent)
    {
        ushort type = ReadUInt16(inputEvent, InputEventTypeOffset);
        if (type != EV_KEY)
            return;

        ushort code = ReadUInt16(inputEvent, InputEventCodeOffset);
        if (code is not (KEY_LEFTSHIFT or KEY_RIGHTSHIFT))
            return;

        int value = ReadInt32(inputEvent, InputEventValueOffset);
        if (value is not (0 or 1))
            return;

        int down = value == 1 ? 1 : 0;
        if (code == KEY_LEFTSHIFT)
            Volatile.Write(ref _leftShiftDown, down);
        else
            Volatile.Write(ref _rightShiftDown, down);
    }

    private ConsoleModifiers GetCurrentModifiers() =>
        Volatile.Read(ref _leftShiftDown) != 0 || Volatile.Read(ref _rightShiftDown) != 0
            ? ConsoleModifiers.Shift
            : default;

    private static bool TryHasShiftCapability(int fd, out string? error)
    {
        byte[] keyBits = new byte[64];
        nint result = ioctl(fd, EvIocGetBit(EV_KEY, keyBits.Length), keyBits);
        if (result < 0)
        {
            error = "EV_KEY capability check failed: " + DescribeErrno(Marshal.GetLastPInvokeError());
            return false;
        }

        error = null;
        return IsBitSet(keyBits, KEY_LEFTSHIFT) || IsBitSet(keyBits, KEY_RIGHTSHIFT);
    }

    private static string? TryReadDeviceName(int fd)
    {
        byte[] name = new byte[256];
        nint result = ioctl(fd, EvIocGetName(name.Length), name);
        if (result < 0)
            return null;

        int length = Array.IndexOf(name, (byte)0);
        if (length < 0)
            length = name.Length;

        return length == 0 ? null : Encoding.UTF8.GetString(name, 0, length);
    }

    private static bool IsBitSet(byte[] bits, ushort bit)
    {
        int byteIndex = bit / 8;
        int bitIndex = bit % 8;
        return byteIndex < bits.Length && (bits[byteIndex] & (1 << bitIndex)) != 0;
    }

    private static uint EvIocGetBit(ushort eventType, int length) =>
        Ioc(IocRead, 'E', 0x20 + eventType, length);

    private static uint EvIocGetName(int length) =>
        Ioc(IocRead, 'E', 0x06, length);

    private static uint Ioc(uint direction, int type, int number, int size) =>
        (direction << (int)IocDirShift) |
        ((uint)type << (int)IocTypeShift) |
        ((uint)number << (int)IocNrShift) |
        ((uint)size << (int)IocSizeShift);

    private static string DescribeErrno(int errno) =>
        errno switch
        {
            EACCES => "permission denied",
            EPERM => "operation not permitted",
            2 => "not found",
            6 => "no such device or address",
            19 => "no such device",
            _ => $"errno {errno}",
        };

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.ToUInt16(bytes.Slice(offset, sizeof(ushort)));

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.ToInt32(bytes.Slice(offset, sizeof(int)));

    private static int InputEventSize => IntPtr.Size == 8 ? 24 : 16;

    private static int InputEventTypeOffset => IntPtr.Size == 8 ? 16 : 8;

    private static int InputEventCodeOffset => InputEventTypeOffset + 2;

    private static int InputEventValueOffset => InputEventTypeOffset + 4;

    private const uint IocNrShift = 0;
    private const uint IocTypeShift = 8;
    private const uint IocSizeShift = 16;
    private const uint IocDirShift = 30;
    private const uint IocRead = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, nuint nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern nint ioctl(int fd, uint request, byte[] data);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;

        public PollFd(int fd, short events, short revents)
        {
            Fd = fd;
            Events = events;
            Revents = revents;
        }
    }

    private sealed record DeviceHandle(string Path, int FileDescriptor);
}
