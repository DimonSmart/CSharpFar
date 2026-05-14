using System.Text;

namespace CSharpFar.App.Viewer;

internal sealed record ScannedLine(long StartOffset, long NextOffset, string Text);

internal sealed record ScannedLines(IReadOnlyList<ScannedLine> Lines, long NextOffset);

internal sealed class LineScanner
{
    private const int MaxLineScanBytes = 4 * 1024 * 1024;

    private readonly BlockCache _cache;
    private readonly IFileByteReader _reader;
    private readonly bool _isUtf16;
    private readonly bool _isUtf16BigEndian;

    private LineScanner(
        BlockCache cache,
        IFileByteReader reader,
        Encoding encoding,
        long contentStartOffset,
        bool isBinary,
        bool isUtf16,
        bool isUtf16BigEndian)
    {
        _cache = cache;
        _reader = reader;
        Encoding = encoding;
        ContentStartOffset = contentStartOffset;
        IsBinary = isBinary;
        _isUtf16 = isUtf16;
        _isUtf16BigEndian = isUtf16BigEndian;
    }

    public Encoding Encoding { get; }
    public long ContentStartOffset { get; }
    public bool IsBinary { get; }
    public int NewLineWidth => _isUtf16 ? 2 : 1;

    public static async Task<LineScanner> CreateAsync(
        BlockCache cache,
        IFileByteReader reader,
        CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<byte> initial = reader.Length == 0
            ? ReadOnlyMemory<byte>.Empty
            : await cache.ReadBlockAsync(0, cancellationToken).ConfigureAwait(false);

        var detection = DetectEncoding(initial.Span);
        return new LineScanner(
            cache,
            reader,
            detection.Encoding,
            detection.ContentStartOffset,
            detection.IsBinary,
            detection.IsUtf16,
            detection.IsUtf16BigEndian);
    }

    public async Task<ScannedLines> ReadLinesAsync(
        long startOffset,
        int lineCount,
        int maxBytesPerLine,
        CancellationToken cancellationToken = default)
    {
        if (lineCount <= 0)
            return new ScannedLines([], Math.Max(ContentStartOffset, startOffset));

        long offset = Math.Clamp(startOffset, ContentStartOffset, _reader.Length);
        var lines = new List<ScannedLine>(lineCount);

        for (int i = 0; i < lineCount && offset < _reader.Length; i++)
        {
            var line = await ReadLineAsync(offset, maxBytesPerLine, cancellationToken)
                .ConfigureAwait(false);
            lines.Add(line);

            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
        }

        return new ScannedLines(lines, offset);
    }

    public async Task<long> FindLineStartAtOrBeforeAsync(
        long offset,
        CancellationToken cancellationToken = default)
    {
        if (offset <= ContentStartOffset)
            return ContentStartOffset;

        long clampedOffset = Math.Min(offset, _reader.Length);
        long? previousNewLine = await FindPreviousNewLineBeforeAsync(clampedOffset, cancellationToken)
            .ConfigureAwait(false);
        return previousNewLine.HasValue
            ? Math.Min(_reader.Length, previousNewLine.Value + NewLineWidth)
            : ContentStartOffset;
    }

    public async Task<long> FindPreviousLineStartAsync(
        long lineStartOffset,
        CancellationToken cancellationToken = default)
    {
        if (lineStartOffset <= ContentStartOffset)
            return ContentStartOffset;

        long searchBeforeDelimiter = Math.Max(ContentStartOffset, lineStartOffset - NewLineWidth);
        long? previousNewLine = await FindPreviousNewLineBeforeAsync(searchBeforeDelimiter, cancellationToken)
            .ConfigureAwait(false);
        return previousNewLine.HasValue
            ? Math.Min(_reader.Length, previousNewLine.Value + NewLineWidth)
            : ContentStartOffset;
    }

    public async Task<long> FindTailTopOffsetAsync(
        int visibleLines,
        CancellationToken cancellationToken = default)
    {
        long effectiveEnd = await GetEffectiveEndOffsetAsync(cancellationToken).ConfigureAwait(false);
        long lineStart = await FindLineStartAtOrBeforeAsync(effectiveEnd, cancellationToken)
            .ConfigureAwait(false);

        for (int i = 1; i < visibleLines; i++)
        {
            long previous = await FindPreviousLineStartAsync(lineStart, cancellationToken)
                .ConfigureAwait(false);
            if (previous == lineStart)
                break;

            lineStart = previous;
        }

        return lineStart;
    }

    public async Task<long> FindLineOffsetAsync(
        long targetLineNumber,
        SparseLineIndex lineIndex,
        CancellationToken cancellationToken = default)
    {
        if (targetLineNumber <= 1)
            return ContentStartOffset;

        var checkpoint = lineIndex.FindNearestLine(targetLineNumber);
        long currentLine = Math.Max(1, checkpoint.LineNumber);
        long offset = checkpoint.ByteOffset == 0 ? ContentStartOffset : checkpoint.ByteOffset;

        while (currentLine < targetLineNumber && offset < _reader.Length)
        {
            var line = await ReadLineAsync(offset, maxBytesPerLine: 0, cancellationToken)
                .ConfigureAwait(false);
            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
            currentLine++;
            lineIndex.Add(currentLine, offset);
        }

        return offset;
    }

    private async Task<ScannedLine> ReadLineAsync(
        long startOffset,
        int maxBytesPerLine,
        CancellationToken cancellationToken)
    {
        long offset = startOffset;
        long scanOffset = offset;
        long length = _reader.Length;
        int scannedBytes = 0;
        int captureLimit = Math.Max(0, maxBytesPerLine);
        var captured = new List<byte>(Math.Min(captureLimit, 4096));

        while (scanOffset < length && scannedBytes < MaxLineScanBytes)
        {
            ReadOnlyMemory<byte> block = await _cache.ReadBlockAsync(scanOffset, cancellationToken)
                .ConfigureAwait(false);
            if (block.IsEmpty)
                break;

            long blockStart = scanOffset / _cache.BlockSize * _cache.BlockSize;
            int blockOffset = (int)(scanOffset - blockStart);
            ReadOnlySpan<byte> available = block.Span[blockOffset..];
            if (available.IsEmpty)
                break;

            int scanLength = Math.Min(available.Length, MaxLineScanBytes - scannedBytes);
            ReadOnlySpan<byte> scanSpan = available[..scanLength];
            int newLineIndex = FindNewLine(scanSpan, scanOffset);
            int bytesBeforeNewLine = newLineIndex >= 0 ? newLineIndex : scanSpan.Length;

            int remainingCapture = captureLimit - captured.Count;
            if (remainingCapture > 0)
            {
                int captureBytes = Math.Min(bytesBeforeNewLine, remainingCapture);
                for (int i = 0; i < captureBytes; i++)
                    captured.Add(scanSpan[i]);
            }

            if (newLineIndex >= 0)
            {
                TrimTrailingCarriageReturn(captured);
                long nextOffset = scanOffset + newLineIndex + NewLineWidth;
                string text = DecodeLine(captured);
                return new ScannedLine(startOffset, nextOffset, text);
            }

            scanOffset += scanSpan.Length;
            scannedBytes += scanSpan.Length;
        }

        TrimTrailingCarriageReturn(captured);
        string finalText = DecodeLine(captured);
        long finalNextOffset = scanOffset > startOffset
            ? scanOffset
            : Math.Min(length, startOffset + NewLineWidth);
        return new ScannedLine(startOffset, finalNextOffset, finalText);
    }

    private async Task<long?> FindPreviousNewLineBeforeAsync(
        long offsetExclusive,
        CancellationToken cancellationToken)
    {
        long searchOffset = Math.Min(offsetExclusive, _reader.Length);
        while (searchOffset > ContentStartOffset)
        {
            long blockStart = (searchOffset - 1) / _cache.BlockSize * _cache.BlockSize;
            ReadOnlyMemory<byte> block = await _cache.ReadBlockAsync(blockStart, cancellationToken)
                .ConfigureAwait(false);
            if (block.IsEmpty)
                break;

            int end = (int)Math.Min(searchOffset - blockStart, block.Length);
            int index = FindLastNewLine(block.Span[..end], blockStart);
            if (index >= 0)
                return blockStart + index;

            searchOffset = blockStart;
        }

        return null;
    }

    private async Task<long> GetEffectiveEndOffsetAsync(CancellationToken cancellationToken)
    {
        long length = _reader.Length;
        if (length <= ContentStartOffset + NewLineWidth)
            return length;

        var buffer = new byte[NewLineWidth];
        int read = await _cache.ReadAsync(length - NewLineWidth, buffer, cancellationToken)
            .ConfigureAwait(false);
        if (read == NewLineWidth && IsNewLine(buffer, 0))
            return length - NewLineWidth;

        return length;
    }

    private int FindNewLine(ReadOnlySpan<byte> bytes, long absoluteOffset)
    {
        if (!_isUtf16)
            return bytes.IndexOf((byte)'\n');

        int firstIndex = FirstAlignedIndex(absoluteOffset);
        for (int i = firstIndex; i + 1 < bytes.Length; i += 2)
        {
            if (IsNewLine(bytes, i))
                return i;
        }

        return -1;
    }

    private int FindLastNewLine(ReadOnlySpan<byte> bytes, long blockStart)
    {
        if (!_isUtf16)
        {
            int start = (int)Math.Max(0, ContentStartOffset - blockStart);
            int index = bytes[start..].LastIndexOf((byte)'\n');
            return index < 0 ? -1 : start + index;
        }

        for (int i = bytes.Length - 2; i >= 0; i--)
        {
            long absoluteOffset = blockStart + i;
            if (absoluteOffset < ContentStartOffset)
                break;
            if ((absoluteOffset - ContentStartOffset) % 2 != 0)
                continue;
            if (IsNewLine(bytes, i))
                return i;
        }

        return -1;
    }

    private int FirstAlignedIndex(long absoluteOffset)
    {
        if (!_isUtf16)
            return 0;

        long delta = absoluteOffset - ContentStartOffset;
        return delta % 2 == 0 ? 0 : 1;
    }

    private bool IsNewLine(ReadOnlySpan<byte> bytes, int index)
    {
        if (!_isUtf16)
            return bytes[index] == (byte)'\n';

        return _isUtf16BigEndian
            ? bytes[index] == 0x00 && bytes[index + 1] == 0x0A
            : bytes[index] == 0x0A && bytes[index + 1] == 0x00;
    }

    private void TrimTrailingCarriageReturn(List<byte> bytes)
    {
        if (!_isUtf16)
        {
            if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
                bytes.RemoveAt(bytes.Count - 1);
            return;
        }

        if (bytes.Count < 2)
            return;

        bool hasCr = _isUtf16BigEndian
            ? bytes[^2] == 0x00 && bytes[^1] == 0x0D
            : bytes[^2] == 0x0D && bytes[^1] == 0x00;
        if (hasCr)
            bytes.RemoveRange(bytes.Count - 2, 2);
    }

    private string DecodeLine(List<byte> bytes) =>
        bytes.Count == 0 ? string.Empty : Encoding.GetString([.. bytes]);

    private static DetectionResult DetectEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return new DetectionResult(new UTF8Encoding(false, false), 3, false, false, false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new DetectionResult(new UnicodeEncoding(false, true, false), 2, false, true, false);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new DetectionResult(new UnicodeEncoding(true, true, false), 2, false, true, true);

        if (LooksLikeUtf16(bytes, bigEndian: false))
            return new DetectionResult(new UnicodeEncoding(false, false, false), 0, false, true, false);

        if (LooksLikeUtf16(bytes, bigEndian: true))
            return new DetectionResult(new UnicodeEncoding(true, false, false), 0, false, true, true);

        if (LooksBinary(bytes))
            return new DetectionResult(new UTF8Encoding(false, false), 0, true, false, false);

        if (IsValidUtf8(bytes))
            return new DetectionResult(new UTF8Encoding(false, false), 0, false, false, false);

        return new DetectionResult(Encoding.Default, 0, false, false, false);
    }

    private static bool LooksLikeUtf16(ReadOnlySpan<byte> bytes, bool bigEndian)
    {
        int pairs = Math.Min(bytes.Length / 2, 4096);
        if (pairs < 8)
            return false;

        int zeroCount = 0;
        int printableCount = 0;
        for (int pair = 0; pair < pairs; pair++)
        {
            byte first = bytes[pair * 2];
            byte second = bytes[pair * 2 + 1];
            byte high = bigEndian ? first : second;
            byte low = bigEndian ? second : first;
            if (high == 0)
                zeroCount++;
            if (low is >= 0x09 and <= 0x7E)
                printableCount++;
        }

        return zeroCount > pairs * 3 / 5 && printableCount > pairs / 2;
    }

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return false;

        int controlCount = 0;
        int checkedBytes = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < checkedBytes; i++)
        {
            byte value = bytes[i];
            if (value == 0)
                return true;
            if (value < 0x20 && value is not 0x09 and not 0x0A and not 0x0D and not 0x1B)
                controlCount++;
        }

        return controlCount > checkedBytes / 20;
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private readonly record struct DetectionResult(
        Encoding Encoding,
        long ContentStartOffset,
        bool IsBinary,
        bool IsUtf16,
        bool IsUtf16BigEndian);
}
