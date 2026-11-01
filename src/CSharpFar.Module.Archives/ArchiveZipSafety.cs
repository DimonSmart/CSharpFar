using System.Buffers.Binary;

namespace CSharpFar.Module.Archives;

/// <summary>
/// Conservative write eligibility for ZIP rebuilds. ZipArchive does not expose
/// arbitrary extra fields, signatures or per-entry comments for lossless rewriting.
/// Refuse them rather than silently stripping data from the original archive.
/// ZIP64 and non-Deflate/non-Stored codecs remain readable but not writable.
/// </summary>
internal static class ArchiveZipSafety
{
    private const uint EocdSignature = 0x06054b50;
    private const uint CentralSignature = 0x02014b50;
    private const uint LocalSignature = 0x04034b50;
    private const ushort AllowedFlags = (1 << 3) | (1 << 11);

    public static bool IsRebuildSafe(string path)
    {
        try
        {
            Validate(path);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException
            or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    public static void Validate(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        long length = stream.Length;
        if (length < 22)
            throw new InvalidDataException("Truncated ZIP.");

        int tailSize = (int)Math.Min(length, 65557);
        stream.Position = length - tailSize;
        byte[] tail = reader.ReadBytes(tailSize);
        int eocd = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) == EocdSignature &&
                i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2)) == tail.Length)
            {
                eocd = i;
                break;
            }
        }
        if (eocd < 0)
            throw new InvalidDataException("ZIP end record or archive comment is unsupported.");

        var end = tail.AsSpan(eocd);
        ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(end.Slice(4, 2));
        ushort cdDisk = BinaryPrimitives.ReadUInt16LittleEndian(end.Slice(6, 2));
        ushort onDisk = BinaryPrimitives.ReadUInt16LittleEndian(end.Slice(8, 2));
        ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(end.Slice(10, 2));
        uint centralSize = BinaryPrimitives.ReadUInt32LittleEndian(end.Slice(12, 4));
        uint centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(end.Slice(16, 4));
        if (disk != 0 || cdDisk != 0 || entries != onDisk || entries == ushort.MaxValue ||
            centralOffset == uint.MaxValue || centralSize == uint.MaxValue ||
            (long)centralOffset + centralSize != length - tailSize + eocd)
            throw new InvalidDataException("ZIP64, multi-disk and prefixed archives are read-only.");

        stream.Position = centralOffset;
        for (int i = 0; i < entries; i++)
        {
            long offset = stream.Position;
            if (reader.ReadUInt32() != CentralSignature)
                throw new InvalidDataException("Invalid ZIP central directory.");
            _ = reader.ReadUInt16(); // made by
            ushort neededVersion = reader.ReadUInt16();
            ushort flags = reader.ReadUInt16();
            ushort method = reader.ReadUInt16();
            stream.Position = offset + 28;
            ushort nameLength = reader.ReadUInt16();
            ushort extraLength = reader.ReadUInt16();
            ushort commentLength = reader.ReadUInt16();
            stream.Position = offset + 42;
            uint localOffset = reader.ReadUInt32();
            if (neededVersion > 20 || (flags & ~AllowedFlags) != 0 ||
                method is not (0 or 8) || nameLength == 0 ||
                extraLength != 0 || commentLength != 0 ||
                localOffset == uint.MaxValue)
                throw new InvalidDataException("ZIP contains metadata or compression that cannot be preserved.");

            long next = offset + 46 + nameLength;
            if (next > length)
                throw new InvalidDataException("Invalid ZIP central entry length.");

            stream.Position = localOffset;
            if (reader.ReadUInt32() != LocalSignature)
                throw new InvalidDataException("Invalid ZIP local entry.");
            stream.Position = localOffset + 26;
            ushort localNameLength = reader.ReadUInt16();
            ushort localExtraLength = reader.ReadUInt16();
            if (localNameLength != nameLength || localExtraLength != 0)
                throw new InvalidDataException("ZIP local entry has unsupported extra fields.");

            stream.Position = next;
        }
        if (stream.Position != (long)centralOffset + centralSize)
            throw new InvalidDataException("ZIP central directory has unknown records.");
    }
}
