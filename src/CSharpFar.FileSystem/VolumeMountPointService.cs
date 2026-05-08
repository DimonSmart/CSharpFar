using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>
/// Detects NTFS volume mount points.
/// On Windows uses GetVolumeNameForVolumeMountPoint; other platforms always return
/// IsVolumeMountPoint = false.
/// Symlinks and junctions are NOT treated as mount points by this service.
/// </summary>
public sealed class VolumeMountPointService : IVolumeMountPointService
{
    public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
    {
        var notMounted = new VolumeMountPointInfo { IsVolumeMountPoint = false };

        if (!OperatingSystem.IsWindows())
            return notMounted;

        try
        {
            var info = new DirectoryInfo(directoryPath);
            if (!info.Exists) return notMounted;
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0) return notMounted;

            return TryGetMountPointWindows(directoryPath) ?? notMounted;
        }
        catch
        {
            return notMounted;
        }
    }

    [SupportedOSPlatform("windows")]
    private static VolumeMountPointInfo? TryGetMountPointWindows(string directoryPath)
    {
        string mountPath = directoryPath.TrimEnd('\\', '/') + '\\';

        var volumeNameBuffer = new System.Text.StringBuilder(260);
        if (!GetVolumeNameForVolumeMountPoint(mountPath, volumeNameBuffer, (uint)volumeNameBuffer.Capacity))
            return null;

        string volumeName = volumeNameBuffer.ToString();

        // Get a display-friendly label if possible
        string? volumePath = null;
        try
        {
            var pathBuffer = new System.Text.StringBuilder(260);
            if (GetVolumePathName(mountPath, pathBuffer, (uint)pathBuffer.Capacity))
                volumePath = pathBuffer.ToString();
        }
        catch { /* ignore */ }

        return new VolumeMountPointInfo
        {
            IsVolumeMountPoint = true,
            VolumeName         = volumeName,
            VolumePath         = volumePath,
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string  lpszVolumeMountPoint,
        System.Text.StringBuilder lpszVolumeName,
        uint    cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(
        string  lpszFileName,
        System.Text.StringBuilder lpszVolumePathName,
        uint    cchBufferLength);
}
