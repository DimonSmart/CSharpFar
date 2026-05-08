using System.Runtime.InteropServices;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>
/// Windows-aware implementation of IVolumeInfoService.
/// Uses GetDiskFreeSpaceEx on Windows for correct results with mount points and UNC paths;
/// falls back to DriveInfo on other platforms.
/// </summary>
public sealed class VolumeInfoService : IVolumeInfoService
{
    public VolumeSpaceInfo GetSpaceInfo(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetDiskFreeSpaceEx(path, out ulong freeBytesAvailable, out ulong totalBytes, out _))
                throw new IOException($"GetDiskFreeSpaceEx failed for '{path}'");

            return new VolumeSpaceInfo
            {
                Path               = path,
                FreeBytesAvailable = (long)freeBytesAvailable,
                TotalBytes         = (long)totalBytes,
                VolumeLabel        = TryGetVolumeLabel(path),
            };
        }

        // Non-Windows fallback
        string root = Path.GetPathRoot(path) ?? path;
        var drive = new DriveInfo(root);
        return new VolumeSpaceInfo
        {
            Path               = path,
            FreeBytesAvailable = drive.AvailableFreeSpace,
            TotalBytes         = drive.TotalSize,
            VolumeLabel        = drive.VolumeLabel,
        };
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
        string      lpDirectoryName,
        out ulong   lpFreeBytesAvailable,
        out ulong   lpTotalNumberOfBytes,
        out ulong   lpTotalNumberOfFreeBytes);
}
