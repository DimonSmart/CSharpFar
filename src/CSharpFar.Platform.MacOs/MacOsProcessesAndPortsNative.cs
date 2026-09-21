using System.Runtime.InteropServices;

namespace CSharpFar.Platform.MacOs;

internal readonly record struct MacOsNativeResult(int Value, int Error);

internal interface IMacOsProcessesAndPortsNative
{
    MacOsNativeResult ListAllPids(IntPtr buffer, int bufferSize);
    MacOsNativeResult PidInfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize);
    MacOsNativeResult PidFdInfo(int pid, int fd, int flavor, IntPtr buffer, int bufferSize);
    MacOsNativeResult PidPath(int pid, IntPtr buffer, int bufferSize);
}

internal enum MacOsNativeErrorKind
{
    None,
    NotFound,
    AccessDenied,
    NoMemory,
    Overflow,
    Other,
}

internal static class MacOsNativeError
{
    internal static MacOsNativeErrorKind Classify(int error) => error switch
    {
        0 => MacOsNativeErrorKind.None,
        MacOsProcessesAndPortsNative.ESrch => MacOsNativeErrorKind.NotFound,
        MacOsProcessesAndPortsNative.EPerm or MacOsProcessesAndPortsNative.EAccess => MacOsNativeErrorKind.AccessDenied,
        MacOsProcessesAndPortsNative.ENoMem => MacOsNativeErrorKind.NoMemory,
        MacOsProcessesAndPortsNative.EOverflow => MacOsNativeErrorKind.Overflow,
        _ => MacOsNativeErrorKind.Other,
    };
}

internal sealed class MacOsLibProc : IMacOsProcessesAndPortsNative
{
    public MacOsNativeResult ListAllPids(IntPtr buffer, int bufferSize)
    {
        Marshal.SetLastPInvokeError(0);
        int value = MacOsProcessesAndPortsNative.proc_listallpids(buffer, bufferSize);
        return new(value, Marshal.GetLastPInvokeError());
    }

    public MacOsNativeResult PidInfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize)
    {
        Marshal.SetLastPInvokeError(0);
        int value = MacOsProcessesAndPortsNative.proc_pidinfo(pid, flavor, arg, buffer, bufferSize);
        return new(value, Marshal.GetLastPInvokeError());
    }

    public MacOsNativeResult PidFdInfo(int pid, int fd, int flavor, IntPtr buffer, int bufferSize)
    {
        Marshal.SetLastPInvokeError(0);
        int value = MacOsProcessesAndPortsNative.proc_pidfdinfo(pid, fd, flavor, buffer, bufferSize);
        return new(value, Marshal.GetLastPInvokeError());
    }

    public MacOsNativeResult PidPath(int pid, IntPtr buffer, int bufferSize)
    {
        Marshal.SetLastPInvokeError(0);
        int value = MacOsProcessesAndPortsNative.proc_pidpath(pid, buffer, checked((uint)bufferSize));
        return new(value, Marshal.GetLastPInvokeError());
    }
}

internal static partial class MacOsProcessesAndPortsNative
{
    internal const int ProcPidListFds = 1;
    internal const int ProcPidTBsdInfo = 3;
    internal const int ProcPidFdSocketInfo = 3;
    internal const uint ProxFdTypeSocket = 2;

    internal const int EPerm = 1;
    internal const int ESrch = 3;
    internal const int ENoMem = 12;
    internal const int EAccess = 13;
    internal const int EOverflow = 84;

    [LibraryImport("proc", SetLastError = true)]
    internal static partial int proc_listallpids(IntPtr buffer, int bufferSize);

    [LibraryImport("proc", SetLastError = true)]
    internal static partial int proc_pidinfo(int pid, int flavor, ulong arg, IntPtr buffer, int bufferSize);

    [LibraryImport("proc", SetLastError = true)]
    internal static partial int proc_pidfdinfo(int pid, int fd, int flavor, IntPtr buffer, int bufferSize);

    [LibraryImport("proc", SetLastError = true)]
    internal static partial int proc_pidpath(int pid, IntPtr buffer, uint bufferSize);
}
