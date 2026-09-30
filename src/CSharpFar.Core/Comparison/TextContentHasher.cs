using System.Security.Cryptography;
using System.Text;

namespace CSharpFar.Core.Comparison;

public sealed record TextContentHashResult(string Digest, bool IsBinary);

public sealed class TextContentHasher
{
    private const int CharacterBufferSize = 4096;

    private readonly IComparisonFileSystem _fileSystem;
    private readonly FileContentHasher _rawHasher;
    private readonly int _byteBufferSize;
    private readonly int _detectionPrefixSize;

    public TextContentHasher(IComparisonFileSystem? fileSystem = null)
        : this(
            fileSystem ?? new LocalComparisonFileSystem(),
            TextCanonicalReader.DefaultByteBufferSize,
            TextCanonicalReader.DefaultDetectionPrefixSize)
    {
    }

    internal TextContentHasher(
        IComparisonFileSystem fileSystem,
        int byteBufferSize,
        int detectionPrefixSize)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
        _rawHasher = new FileContentHasher(fileSystem);
        _byteBufferSize = byteBufferSize;
        _detectionPrefixSize = detectionPrefixSize;
    }

    public TextContentHashResult ComputeSha256(
        FileEntry entry,
        ComparisonOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(options);

        using var reader = new TextCanonicalReader(
            _fileSystem.OpenRead(entry.FullPath),
            options,
            cancellationToken,
            _byteBufferSize,
            _detectionPrefixSize);

        if (reader.Detection.IsBinary)
        {
            return new TextContentHashResult(
                _rawHasher.ComputeSha256(entry, cancellationToken),
                IsBinary: true);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(
        [
            (byte)'C', (byte)'S', (byte)'F', (byte)'T', 1,
            (byte)options.TextLineEndings,
            (byte)options.TextWhitespace,
            (byte)options.TextBom,
            (byte)(options.TextBom == TextBomComparison.Exact &&
                   reader.Detection.HasByteOrderMark ? 1 : 0),
        ]);

        char[] chars = new char[CharacterBufferSize];
        var utf8 = new UTF8Encoding(false, false);
        Encoder encoder = utf8.GetEncoder();
        byte[] bytes = new byte[utf8.GetMaxByteCount(chars.Length)];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int charCount = reader.Read(chars, cancellationToken);
            if (charCount == 0)
                break;
            AppendEncoded(hash, encoder, chars.AsSpan(0, charCount), bytes, flush: false);
        }

        AppendEncoded(hash, encoder, ReadOnlySpan<char>.Empty, bytes, flush: true);
        return new TextContentHashResult(Convert.ToHexString(hash.GetHashAndReset()), IsBinary: false);
    }

    private static void AppendEncoded(
        IncrementalHash hash,
        Encoder encoder,
        ReadOnlySpan<char> chars,
        byte[] bytes,
        bool flush)
    {
        int offset = 0;
        do
        {
            encoder.Convert(
                chars[offset..],
                bytes,
                flush,
                out int charsUsed,
                out int bytesUsed,
                out bool completed);
            if (bytesUsed > 0)
                hash.AppendData(bytes.AsSpan(0, bytesUsed));
            offset += charsUsed;
            if (completed)
                break;
        }
        while (offset < chars.Length || flush);
    }
}
