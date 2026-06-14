namespace CSharpFar.FileSystem;

public sealed class UnixMountInfoReader
{
    private readonly string _path;

    public UnixMountInfoReader(string path = "/proc/self/mountinfo")
    {
        _path = path;
    }

    public IReadOnlyList<UnixMountInfoEntry> Read()
    {
        try
        {
            return Parse(File.ReadLines(_path));
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<UnixMountInfoEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<UnixMountInfoEntry>();
        foreach (string line in lines)
        {
            if (TryParseLine(line, out var entry))
                entries.Add(entry);
        }

        return entries;
    }

    internal static bool IsUserVisible(UnixMountInfoEntry entry)
    {
        string mount = NormalizeMountPoint(entry.MountPoint);
        string fs = entry.FileSystemType;

        if (mount == "/")
            return true;
        if (mount.StartsWith("/mnt/", StringComparison.Ordinal) && mount.Count(static ch => ch == '/') == 2)
            return true;
        if (mount.StartsWith("/home", StringComparison.Ordinal))
            return true;
        if (mount.StartsWith("/media", StringComparison.Ordinal))
            return true;
        if (mount.StartsWith("/run/media", StringComparison.Ordinal))
            return true;
        if (IsNetworkFileSystem(fs))
            return true;

        if (mount.StartsWith("/proc", StringComparison.Ordinal) ||
            mount.StartsWith("/sys", StringComparison.Ordinal) ||
            mount.StartsWith("/dev", StringComparison.Ordinal) ||
            mount.StartsWith("/run", StringComparison.Ordinal) ||
            mount.StartsWith("/snap", StringComparison.Ordinal))
        {
            return false;
        }

        return fs is not ("proc" or "sysfs" or "devtmpfs" or "devpts" or "cgroup" or "cgroup2" or "securityfs" or "debugfs" or "tracefs" or "pstore" or "efivarfs" or "mqueue" or "hugetlbfs" or "configfs" or "fusectl" or "tmpfs");
    }

    internal static bool IsNetworkFileSystem(string fileSystemType) =>
        fileSystemType is "nfs" or "nfs4" or "cifs" or "smb3" or "sshfs";

    internal static string NormalizeMountPoint(string mountPoint)
    {
        string full = Path.GetFullPath(mountPoint);
        return full.Length > 1 ? full.TrimEnd('/') : full;
    }

    private static bool TryParseLine(string line, out UnixMountInfoEntry entry)
    {
        entry = default!;
        string[] sections = line.Split(" - ", 2, StringSplitOptions.None);
        if (sections.Length != 2)
            return false;

        string[] left = sections[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] right = sections[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (left.Length < 5 || right.Length < 3)
            return false;

        entry = new UnixMountInfoEntry(
            Source: Unescape(right[1]),
            MountPoint: Unescape(left[4]),
            FileSystemType: right[0]);
        return true;
    }

    private static string Unescape(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\\([0-7]{3})",
            match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());
    }
}
