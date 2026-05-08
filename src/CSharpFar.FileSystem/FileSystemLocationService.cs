using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

/// <summary>
/// Classifies a file-system path as network, removable, fixed, etc.
/// UNC paths are always network; otherwise uses DriveInfo.DriveType.
/// Errors are silently swallowed – callers get safe defaults.
/// </summary>
public sealed class FileSystemLocationService : IFileSystemLocationService
{
    public FileSystemLocationInfo GetLocationInfo(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);

            // UNC path → always network
            if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                path.StartsWith("//",  StringComparison.Ordinal))
            {
                return new FileSystemLocationInfo
                {
                    Path              = path,
                    IsNetworkDrive    = true,
                    IsRemovableDrive  = false,
                    IsFixedDrive      = false,
                    RootPath          = root,
                };
            }

            if (root == null)
                return MakeUnknown(path);

            var drive = new DriveInfo(root);

            return new FileSystemLocationInfo
            {
                Path             = path,
                IsNetworkDrive   = drive.DriveType == DriveType.Network,
                IsRemovableDrive = drive.DriveType == DriveType.Removable,
                IsFixedDrive     = drive.DriveType == DriveType.Fixed,
                RootPath         = root,
            };
        }
        catch
        {
            return MakeUnknown(path);
        }
    }

    private static FileSystemLocationInfo MakeUnknown(string path) =>
        new()
        {
            Path             = path,
            IsNetworkDrive   = false,
            IsRemovableDrive = false,
            IsFixedDrive     = false,
            RootPath         = Path.GetPathRoot(path),
        };
}
