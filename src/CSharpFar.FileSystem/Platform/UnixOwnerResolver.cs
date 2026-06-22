using System.Runtime.InteropServices;

namespace CSharpFar.FileSystem.Platform;

internal static class UnixOwnerResolver
{
    private const string LibC = "libc";
    private static readonly object NameLookupLock = new();

    internal static UnixOwnerIdentity? TryResolve(string path)
    {
        try
        {
            if (Stat(path, out LinuxStat value) != 0)
                return null;

            int? uid = ToInt(value.Uid);
            int? gid = ToInt(value.Gid);
            string? ownerName;
            string? groupName;
            lock (NameLookupLock)
            {
                ownerName = ReadName(GetPwUid(value.Uid));
                groupName = ReadName(GetGrGid(value.Gid));
            }

            return new UnixOwnerIdentity(
                uid,
                gid,
                ownerName ?? uid?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                groupName ?? gid?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static int? ToInt(uint value) => value <= int.MaxValue ? (int)value : null;

    private static string? ReadName(nint entry)
    {
        if (entry == 0)
            return null;
        nint name = Marshal.ReadIntPtr(entry);
        return name == 0 ? null : Marshal.PtrToStringUTF8(name);
    }

    [DllImport(LibC, EntryPoint = "stat", SetLastError = true)]
    private static extern int Stat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out LinuxStat value);

    [DllImport(LibC, EntryPoint = "getpwuid", SetLastError = true)]
    private static extern nint GetPwUid(uint uid);

    [DllImport(LibC, EntryPoint = "getgrgid", SetLastError = true)]
    private static extern nint GetGrGid(uint gid);

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStat
    {
        public ulong Device;
        public ulong Inode;
        public ulong HardLinkCount;
        public uint Mode;
        public uint Uid;
        public uint Gid;
        private readonly int _padding;
        public ulong SpecialDevice;
        public long Size;
        public long BlockSize;
        public long BlockCount;
        public long AccessSeconds;
        public long AccessNanoseconds;
        public long ModificationSeconds;
        public long ModificationNanoseconds;
        public long ChangeSeconds;
        public long ChangeNanoseconds;
        private readonly long _reserved0;
        private readonly long _reserved1;
        private readonly long _reserved2;
    }
}

internal sealed record UnixOwnerIdentity(int? Uid, int? Gid, string? OwnerName, string? GroupName);
