using System.Globalization;

namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private ViewerRenderKey? _requestedRenderKey;
    private ViewerPreparedContent? _preparedRenderContent;

    private ViewerPreparedContent? GetOrRequestRenderContent(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width)
    {
        long length = reader.Length;
        var key = new ViewerRenderKey(
            sourcePath,
            state.LineScanner,
            state.BlockCache,
            state.TopByteOffset,
            state.TopWrappedSegmentIndex,
            state.HorizontalOffset,
            state.ViewMode,
            state.WrapLines,
            state.WordWrap,
            state.PresentationMode,
            state.SearchMatch,
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
            cancellationToken => PrepareRenderContentAsync(key, cancellationToken),
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

        var presentation = new ViewerPresentationSession();

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
            IReadOnlyList<PresentedLine> presented = await presentation
                .PresentAsync(
                    key.PresentationMode,
                    key.SourcePath,
                    key.Scanner,
                    scanned.Lines,
                    key.Width,
                    cancellationToken)
                .ConfigureAwait(false);
            var resolved = presented
                .Select(line => ResolvePresentationForSearch(line, key.SearchMatch))
                .ToArray();
            return new ViewerPreparedTextContent(key, scanned, resolved);
        }

        List<ScannedLine> candidates = await ReadWrappedCandidateLinesAsync(
                key.Scanner,
                key.TopByteOffset,
                key.ContentHeight,
                key.Length,
                cancellationToken)
            .ConfigureAwait(false);

        var layouts = new Dictionary<long, WrappedLineLayout>();
        var streaming = new Dictionary<long, LongWrappedLineLayout>();
        bool firstPhysicalLine = true;
        int index = 0;
        while (index < candidates.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RequiresStreamingWrappedLayout(candidates[index]))
            {
                ScannedLine line = candidates[index];
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
                firstPhysicalLine = false;
                index++;
                continue;
            }

            int groupStart = index;
            while (index < candidates.Count && !RequiresStreamingWrappedLayout(candidates[index]))
                index++;

            ScannedLine[] group = candidates
                .Skip(groupStart)
                .Take(index - groupStart)
                .ToArray();
            IReadOnlyList<PresentedLine> presented = await presentation
                .PresentAsync(
                    key.PresentationMode,
                    key.SourcePath,
                    key.Scanner,
                    group,
                    key.Width,
                    cancellationToken)
                .ConfigureAwait(false);

            for (int i = 0; i < group.Length; i++)
            {
                PresentedLine resolved = ResolvePresentationForSearch(presented[i], key.SearchMatch);
                layouts[group[i].StartOffset] = CreateWrappedLineLayout(
                    resolved,
                    key.WordWrap,
                    key.Width);
                firstPhysicalLine = false;
            }
        }

        return new ViewerPreparedWrappedContent(key, candidates, layouts, streaming);
    }

    private static async Task<List<ScannedLine>> ReadWrappedCandidateLinesAsync(
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
        ScannedLines Scanned,
        IReadOnlyList<PresentedLine> Presented) : ViewerPreparedContent(Key);

    private sealed record ViewerPreparedWrappedContent(
        ViewerRenderKey Key,
        IReadOnlyList<ScannedLine> Lines,
        IReadOnlyDictionary<long, WrappedLineLayout> Layouts,
        IReadOnlyDictionary<long, LongWrappedLineLayout> StreamingLayouts) : ViewerPreparedContent(Key);

    private sealed record ViewerPreparedHexContent(
        ViewerRenderKey Key,
        IReadOnlyList<ScannedLine> Rows,
        long NextOffset) : ViewerPreparedContent(Key);

    private sealed record ViewerRenderKey(
        string SourcePath,
        LineScanner Scanner,
        BlockCache Cache,
        long TopByteOffset,
        int TopWrappedSegmentIndex,
        int HorizontalOffset,
        LargeFileViewMode ViewMode,
        bool WrapLines,
        bool WordWrap,
        ViewerPresentationMode PresentationMode,
        ViewerSearchMatch? SearchMatch,
        int ContentHeight,
        int Width,
        long Length);
}
