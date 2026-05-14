using System.Text;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

internal sealed record ScannedLine(long StartOffset, long NextOffset, string Text);

internal sealed record ScannedLines(IReadOnlyList<ScannedLine> Lines, long NextOffset);

internal sealed class LineScanner
{
    private const int MaxLineScanBytes = 4 * 1024 * 1024;
    private const int MaxEncodingSampleBytes = 64 * 1024;
    private const int LineScanChunkBytes = 8192;

    private readonly BlockCache _cache;
    private readonly IFileByteReader _reader;
    private readonly bool _isUtf16;
    private readonly bool _isUtf16BigEndian;

    private LineScanner(
        BlockCache cache,
        IFileByteReader reader,
        EncodingDetectionResult detection)
    {
        _cache = cache;
        _reader = reader;
        Detection = detection;
        Encoding = detection.Encoding;
        ContentStartOffset = detection.ContentStartLength;
        IsBinary = detection.IsBinary;
        _isUtf16 = detection.IsUtf16;
        _isUtf16BigEndian = detection.IsUtf16BigEndian;
    }

    public EncodingDetectionResult Detection { get; }
    public Encoding Encoding { get; }
    public long ContentStartOffset { get; }
    public bool IsBinary { get; }
    public string EncodingDisplayName => Detection.DisplayName;
    public int NewLineWidth => _isUtf16 ? 2 : 1;

    public static async Task<LineScanner> CreateAsync(
        BlockCache cache,
        IFileByteReader reader,
        TextEncodingSelection? encodingSelection = null,
        CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<byte> initial = reader.Length == 0
            ? ReadOnlyMemory<byte>.Empty
            : await cache.ReadBlockAsync(0, cancellationToken).ConfigureAwait(false);

        var sample = initial.Span[..Math.Min(initial.Length, MaxEncodingSampleBytes)];
        var detection = TextEncodingDetector.Detect(sample, encodingSelection);
        return new LineScanner(cache, reader, detection);
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
            int requestedBytes = GetLineScanChunkSize(scanOffset, length, MaxLineScanBytes - scannedBytes);
            if (requestedBytes <= 0)
                break;

            var buffer = new byte[requestedBytes];
            int read = await _cache.ReadAsync(scanOffset, buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            int scanLength = GetUsableLineScanLength(scanOffset, read, length);
            if (scanLength <= 0)
                break;

            ReadOnlySpan<byte> scanSpan = buffer.AsSpan(0, scanLength);
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

    private int GetLineScanChunkSize(long scanOffset, long length, int remainingLimit)
    {
        int requestedBytes = (int)Math.Min(LineScanChunkBytes, Math.Min(length - scanOffset, remainingLimit));
        if (_isUtf16 && requestedBytes == 1 && scanOffset + 1 < length && remainingLimit > 1)
            return 2;

        return requestedBytes;
    }

    private int GetUsableLineScanLength(long scanOffset, int read, long length)
    {
        if (!_isUtf16 || scanOffset + read >= length)
            return read;

        long endDelta = scanOffset + read - ContentStartOffset;
        return endDelta % 2 == 0 ? read : read - 1;
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

}
