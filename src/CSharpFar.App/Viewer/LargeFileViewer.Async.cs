namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private ViewerRenderKey? _requestedRenderKey;
    private ViewerPreparedContent? _preparedRenderContent;

    private ViewerPreparedContent? GetOrRequestRenderContent(
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        long length = reader.Length;
        var key = new ViewerRenderKey(
            state.LineScanner,
            state.BlockCache,
            state.TopByteOffset,
            state.TopWrappedSegmentIndex,
            state.HorizontalOffset,
            state.ViewMode,
            state.WrapLines,
            state.WordWrap,
            Math.Max(0, contentHeight),
            Math.Max(0, width),
            length);

        if (_preparedRenderContent is { } prepared && prepared.Key == key)
            return prepared;

        if (_requestedRenderKey == key)
            return null;

        _requestedRenderKey = key;
        _preparedRenderContent = null;
        long version = _async.StateVersion;

        _async.StartLatest(
            "render",
            version,
            cancellationToken => PrepareRenderContentAsync(key, reader, state, cancellationToken),
            prepared =>
            {
                if (prepared.Key != _requestedRenderKey)
                    return;

                _preparedRenderContent = prepared;
            },
            _ =>
            {
                if (_requestedRenderKey == key)
                    _requestedRenderKey = null;
            });

        return null;
    }

    private async Task<ViewerPreparedContent> PrepareRenderContentAsync(
        ViewerRenderKey key,
        IFileByteReader reader,
        LargeFileViewerState state,
        CancellationToken cancellationToken)
    {
        if (key.ViewMode == LargeFileViewMode.Hex)
        {
            long offset = Math.Clamp(key.TopByteOffset, 0, key.Length);
            int requested = (int)Math.Min(
                (long)key.ContentHeight * BinaryBytesPerRow,
                Math.Max(0, key.Length - offset));
            var bytes = new byte[requested];
            int read = requested == 0
                ? 0
                : await key.Cache.ReadAsync(offset, bytes, cancellationToken).ConfigureAwait(false);
            if (read != bytes.Length)
                Array.Resize(ref bytes, read);

            var rows = new List<ScannedLine>(key.ContentHeight);
            for (int row = 0; row < key.ContentHeight; row++)
            {
                long rowOffset = offset + (long)row * BinaryBytesPerRow;
                int byteIndex = row * BinaryBytesPerRow;
                int rowLength = Math.Clamp(bytes.Length - byteIndex, 0, BinaryBytesPerRow);
                string text = rowLength > 0
                    ? FormatHexRow(bytes.AsSpan(byteIndex, rowLength), rowOffset)
                    : string.Empty;
                rows.Add(new ScannedLine(
                    rowOffset,
                    Math.Min(key.Length, rowOffset + BinaryBytesPerRow),
                    text));
            }

            long nextOffset = Math.Min(
                key.Length,
                offset + (long)key.ContentHeight * BinaryBytesPerRow);
            return new ViewerPreparedHexContent(key, rows, nextOffset);
        }

        if (!key.WrapLines)
        {
            int bytesPerLine = Math.Max(256, (key.HorizontalOffset + key.Width + 32) * 4);
            ScannedLines scanned = await key.Scanner
                .ReadLinesAsync(
                    key.TopByteOffset,
                    key.ContentHeight,
                    bytesPerLine,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ViewerPreparedTextContent(key, scanned);
        }

        List<ScannedLine> candidates = await ReadWrappedCandidateLinesAsync(
                reader,
                key.Scanner,
                key.TopByteOffset,
                key.ContentHeight,
                key.Length,
                cancellationToken)
            .ConfigureAwait(false);

        var streaming = new Dictionary<long, LongWrappedLineLayout>();
        bool firstPhysicalLine = true;
        foreach (ScannedLine line in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RequiresStreamingWrappedLayout(line))
            {
                int firstSegment = firstPhysicalLine
                    ? Math.Max(0, key.TopWrappedSegmentIndex)
                    : 0;
                var builder = new StreamingWrappedLineBuilder(
                    Math.Max(1, key.Width),
                    key.WordWrap,
                    firstSegment,
                    Math.Max(1, key.ContentHeight));
                await key.Scanner
                    .VisitLineTextChunksAsync(line, builder.Append, cancellationToken)
                    .ConfigureAwait(false);
                streaming[line.StartOffset] = builder.Complete();
            }

            firstPhysicalLine = false;
        }

        return new ViewerPreparedWrappedContent(key, candidates, streaming);
    }

    private static async Task<List<ScannedLine>> ReadWrappedCandidateLinesAsync(
        IFileByteReader reader,
        LineScanner scanner,
        long topByteOffset,
        int contentHeight,
        long length,
        CancellationToken cancellationToken)
    {
        int maxLines = Math.Max(1, contentHeight);
        var result = new List<ScannedLine>(maxLines);
        long offset = Math.Clamp(topByteOffset, scanner.ContentStartOffset, length);

        while (result.Count < maxLines && offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScannedLines scanned = await scanner
                .ReadLinesAsync(offset, 1, MaxWrappedLineCaptureBytes, cancellationToken)
                .ConfigureAwait(false);
            if (scanned.Lines.Count == 0)
                break;

            ScannedLine line = scanned.Lines[0];
            result.Add(line);
            if (line.NextOffset <= offset)
                break;

            offset = line.NextOffset;
        }

        return result;
    }

    private static string FormatHexRow(ReadOnlySpan<byte> bytes, long offset)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        var hex = new string[BinaryBytesPerRow];
        var ascii = new char[BinaryBytesPerRow];
        for (int i = 0; i < BinaryBytesPerRow; i++)
        {
            if (i < bytes.Length)
            {
                byte value = bytes[i];
                hex[i] = value.ToString("X2", CultureInfo.InvariantCulture);
                ascii[i] = ToPrintableAscii(value);
            }
            else
            {
                hex[i] = "  ";
                ascii[i] = ' ';
            }
        }

        return $"{offset:X8}  {string.Join(' ', hex)}  {new string(ascii)}";
    }

    private void InvalidatePreparedRender()
    {
        _requestedRenderKey = null;
        _preparedRenderContent = null;
    }

    private abstract record ViewerPreparedContent(ViewerRenderKey Key);

    private sealed record ViewerPreparedTextContent(
        ViewerRenderKey Key,
        ScannedLines Scanned) : ViewerPreparedContent(Key);

    private sealed record ViewerPreparedWrappedContent(
        ViewerRenderKey Key,
        IReadOnlyList<ScannedLine> Lines,
        IReadOnlyDictionary<long, LongWrappedLineLayout> StreamingLayouts) : ViewerPreparedContent(Key);

    private sealed record ViewerPreparedHexContent(
        ViewerRenderKey Key,
        IReadOnlyList<ScannedLine> Rows,
        long NextOffset) : ViewerPreparedContent(Key);

    private sealed record ViewerRenderKey(
        LineScanner Scanner,
        BlockCache Cache,
        long TopByteOffset,
        int TopWrappedSegmentIndex,
        int HorizontalOffset,
        LargeFileViewMode ViewMode,
        bool WrapLines,
        bool WordWrap,
        int ContentHeight,
        int Width,
        long Length);
}
