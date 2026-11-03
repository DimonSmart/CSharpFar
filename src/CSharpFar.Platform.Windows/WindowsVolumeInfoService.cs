using System.Runtime.InteropServices;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Platform.Windows;

/// <summary>
/// Windows volume space provider using GetDiskFreeSpaceEx for mount points and UNC paths.
/// </summary>
public sealed class WindowsVolumeInfoService : IVolumeInfoService
{
    public VolumeSpaceInfo GetSpaceInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetDiskFreeSpaceEx(path, out ulong freeBytesAvailable, out ulong totalBytes, out _))
                throw new IOException($"GetDiskFreeSpaceEx failed for '{path}'");

            return new VolumeSpaceInfo
            {
                Path = path,
                FreeBytesAvailable = (long)freeBytesAvailable,
                TotalBytes = (long)totalBytes,
                VolumeLabel = TryGetVolumeLabel(path),
            };
        }
        throw new PlatformNotSupportedException("Windows volume space enumeration requires Windows.");
    }

    private static string? TryGetVolumeLabel(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);
            if (root == null) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.VolumeLabel : null;
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
