using System.Formats.Tar;
using System.IO.Compression;

namespace CSharpFar.Module.Archives;

/// <summary>
/// TAR write capability is restricted to regular files and directories whose
/// POSIX metadata can be represented by System.Formats.Tar. Unknown PAX keys,
/// links, special files and custom GZip headers are deliberately read-only.
/// </summary>
internal static class ArchiveTarSafety
{
    private static readonly HashSet<string> SupportedPaxKeys = new(StringComparer.Ordinal)
    {
        "path", "mtime", "size", "uid", "gid",
        "uname", "gname", "mode",
    };

    public static bool IsRebuildSafe(string path, string format)
    {
        try
        {
            Validate(path, format);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException
            or NotSupportedException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    public static void Validate(string path, string format)
    {
        using var stream = File.OpenRead(path);
        using var gzip = format == "TAR.GZ" ? OpenSafeGzip(stream) : null;
        using var reader = new TarReader(gzip ?? stream, leaveOpen: true);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile
                or TarEntryType.Directory))
                throw new NotSupportedException("TAR contains a special entry type.");
            if (entry is PaxTarEntry pax && pax.ExtendedAttributes.Keys.Any(key => !SupportedPaxKeys.Contains(key)))
                throw new NotSupportedException("TAR contains unsupported PAX metadata.");
            if (!(entry.EntryType == TarEntryType.Directory && entry.Name is "." or "./"))
                _ = ArchivePath.NormalizeEntry(entry.Name);
        }
    }

    internal static GZipStream OpenSafeGzip(Stream input)
    {
        Span<byte> header = stackalloc byte[10];
        input.ReadExactly(header);
        if (header[0] != 0x1f || header[1] != 0x8b || header[2] != 8 ||
            header[3] != 0 || header[8] > 4)
            throw new NotSupportedException("GZip contains an unsupported header.");
        input.Position = 0;
        return new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
    }

    internal static Dictionary<string, TarMetadata> ReadMetadata(string file, string format)
    {
        using var stream = File.OpenRead(file);
        using var gzip = format == "TAR.GZ" ? OpenSafeGzip(stream) : null;
        using var reader = new TarReader(gzip ?? stream, leaveOpen: true);
        var result = new Dictionary<string, TarMetadata>(StringComparer.Ordinal);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            if (entry.EntryType == TarEntryType.Directory && entry.Name is "." or "./")
                continue;
            string path = ArchivePath.NormalizeEntry(entry.Name);
            result[path] = new TarMetadata(entry.ModificationTime, entry.Mode,
                entry.Uid, entry.Gid,
                (entry as PosixTarEntry)?.UserName,
                (entry as PosixTarEntry)?.GroupName);
        }
        return result;
    }
}

internal sealed record TarMetadata(
    DateTimeOffset Modified, int Mode, int Uid, int Gid,
    string? UserName, string? GroupName);
