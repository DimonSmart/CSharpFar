using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.SevenZip;

namespace CSharpFar.Module.Archives;

/// <summary>
/// Only standalone (non-solid) 7z LZMA-family entries without additional file
/// attributes are eligible for rebuild with SharpCompress 0.50.7.
/// Writer 0.50.7 does not reproduce solid layouts, encryption or rich 7z metadata.
/// </summary>
internal static class ArchiveSevenZipSafety
{
    internal static bool IsRebuildSafe(string file)
    {
        try
        {
            Validate(file);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
            NotSupportedException or UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    internal static void Validate(string file)
    {
        using var input = File.OpenRead(file);
        using var archive = ArchiveFactory.OpenArchive(input);
        if (archive.Type != ArchiveType.SevenZip || archive.IsSolid)
            throw new NotSupportedException("Only non-solid 7z archives are writable.");

        foreach (var entry in archive.Entries)
        {
            if (entry.IsEncrypted || !entry.IsComplete || entry.IsSolid ||
                entry.IsSplitAfter || entry.LinkTarget is not null)
                throw new NotSupportedException("Encrypted, linked and multi-volume 7z files are read-only.");
            if (entry is SevenZipEntry sevenZip &&
                sevenZip.ExtendedAttrib is not null and not 0)
                throw new NotSupportedException("7z extended attributes cannot be preserved.");
            if (entry.Attrib is not null and not 0 and not 0x10 and not 0x80)
                throw new NotSupportedException("7z entry attributes cannot be preserved.");
            if (!entry.IsDirectory && entry.Size > 0 &&
                entry.CompressionType != CompressionType.LZMA)
                throw new NotSupportedException("Only standalone LZMA-family 7z entries can be rewritten.");
            _ = ArchivePath.NormalizeEntry(entry.Key ?? "");
        }
    }
}
