using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>
/// Windows implementation of IVolumeService using System.IO.DriveInfo.
/// </summary>
public sealed class WindowsVolumeService : IVolumeService
{
    public IReadOnlyList<FileSystemVolume> GetVolumes()
    {
        var result = new List<FileSystemVolume>();

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return result; }

        foreach (var drive in drives)
        {
            try
            {
                result.Add(BuildVolume(drive));
            }
            catch
            {
                // Do not let one drive break the whole list.
                result.Add(new FileSystemVolume
                {
                    Id          = drive.Name,
                    DisplayName = MakeDisplayName(drive.Name),
                    RootPath    = drive.RootDirectory.FullName,
                    Kind        = VolumeKind.Unknown,
                    Status      = VolumeStatus.Error,
                    Shortcut    = MakeShortcut(drive.Name),
                });
            }
        }

        return result;
    }

    private static FileSystemVolume BuildVolume(DriveInfo drive)
    {
        var kind = MapKind(drive.DriveType);

        // Network drives: never call IsReady, TotalSize or AvailableFreeSpace synchronously.
        // Those calls can block on unavailable mapped drives and must not delay the dialog.
        if (drive.DriveType == DriveType.Network)
        {
            return new FileSystemVolume
            {
                Id          = drive.Name,
                DisplayName = MakeDisplayName(drive.Name),
                RootPath    = drive.RootDirectory.FullName,
                Kind        = VolumeKind.Network,
                Status      = VolumeStatus.Unchecked,
                TotalBytes  = null,
                FreeBytes   = null,
                Shortcut    = MakeShortcut(drive.Name),
            };
        }

        var status = drive.IsReady ? VolumeStatus.Ready : VolumeStatus.NotReady;

        long? total = null;
        long? free  = null;

        if (drive.IsReady)
        {
            try
            {
                total = drive.TotalSize;
                free  = drive.AvailableFreeSpace;
            }
            catch
            {
                status = VolumeStatus.Error;
            }
        }

        return new FileSystemVolume
        {
            Id          = drive.Name,
            DisplayName = MakeDisplayName(drive.Name),
            RootPath    = drive.RootDirectory.FullName,
            Kind        = kind,
            Status      = status,
            TotalBytes  = total,
            FreeBytes   = free,
            Shortcut    = MakeShortcut(drive.Name),
        };
    }

    private static VolumeKind MapKind(DriveType dt) => dt switch
    {
        DriveType.Fixed     => VolumeKind.Fixed,
        DriveType.Removable => VolumeKind.Removable,
        DriveType.Network   => VolumeKind.Network,
        DriveType.CDRom     => VolumeKind.CdRom,
        DriveType.Ram       => VolumeKind.Ram,
        _                   => VolumeKind.Unknown,
    };

    /// <summary>Converts "C:\" to "C:".</summary>
    private static string MakeDisplayName(string driveName)
    {
        // driveName is e.g. "C:\" on Windows
        if (driveName.Length >= 2 && driveName[1] == ':')
            return driveName[..2];
        return driveName.TrimEnd('/', '\\');
    }

    /// <summary>Returns the single drive letter as shortcut, or null for non-letter roots.</summary>
    private static string? MakeShortcut(string driveName)
    {
        if (driveName.Length >= 2 && char.IsLetter(driveName[0]) && driveName[1] == ':')
            return driveName[0].ToString().ToUpperInvariant();
        return null;
    }
}
