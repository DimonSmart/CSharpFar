using System.Runtime.InteropServices;
using System.Text;
using CSharpFar.FileSystem;

namespace CSharpFar.Platform.MacOs;

/// <summary>Darwin 64-bit statfs snapshot. Reads only cached kernel data (MNT_NOWAIT).</summary>
public sealed class MacOsNativeMountProvider : IMacOsMountProvider
{
    // Darwin arm64 and x64 use __DARWIN_STRUCT_STATFS64 (2168 bytes).
    // Layout: bsize@0, blocks@8, bavail@24, flags@64, fsname@72,
    // mountpoint@88, source@1112, extended flags@2136, reserved@2140.
    private const int StatFsSize = 2168;
    private const int MntNoWait = 2;
    private const uint MntDontBrowse = 0x00100000;
    private const uint MntLocal = 0x00001000;

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "getfsstat", SetLastError = true)]
    private static extern int GetFsStat(IntPtr buffer, int bufferSize, int flags);

    public IReadOnlyList<MacOsMount> GetMounts()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Darwin getfsstat is available only on macOS.");

        int count = GetFsStat(IntPtr.Zero, 0, MntNoWait);
        if (count < 0)
            throw new InvalidOperationException($"getfsstat failed: errno {Marshal.GetLastPInvokeError()}");
        if (count == 0)
            return [];

        // Extra capacity tolerates concurrent mounts. Bound allocation in case of corrupt data.
        int capacity = Math.Min(4096, Math.Max(count + 16, count * 2));
        IntPtr buffer = Marshal.AllocHGlobal(checked(capacity * StatFsSize));
        try
        {
            int actual = GetFsStat(buffer, checked(capacity * StatFsSize), MntNoWait);
            if (actual < 0)
                throw new InvalidOperationException($"getfsstat failed: errno {Marshal.GetLastPInvokeError()}");

            var mounts = new List<MacOsMount>(Math.Min(actual, capacity));
            var bytes = new byte[StatFsSize];
            for (int index = 0; index < Math.Min(actual, capacity); index++)
            {
                Marshal.Copy(IntPtr.Add(buffer, index * StatFsSize), bytes, 0, StatFsSize);
                string path = Decode(bytes, 88, 1024);
                if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
                    continue;

                uint flags = BitConverter.ToUInt32(bytes, 64);
                string fileSystemType = Decode(bytes, 72, 16);
                // Cached getfsstat statistics can be unavailable. Never probe a network volume.
                uint blockSize = BitConverter.ToUInt32(bytes, 0);
                long? total = Size(blockSize, BitConverter.ToUInt64(bytes, 8));
                long? free = Size(blockSize, BitConverter.ToUInt64(bytes, 24));
                mounts.Add(new MacOsMount(path, fileSystemType, (flags & MntDontBrowse) != 0,
                    total, free));
            }
            return mounts;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Decode(byte[] bytes, int start, int length)
    {
        int end = Array.IndexOf(bytes, (byte)0, start, length);
        return Encoding.UTF8.GetString(bytes, start, (end < 0 ? start + length : end) - start);
    }

    private static long? Size(uint blockSize, ulong blocks) =>
        blockSize == 0 || blocks == ulong.MaxValue || blocks > (ulong)long.MaxValue / blockSize
            ? null
            : (long)(blocks * blockSize);
}
