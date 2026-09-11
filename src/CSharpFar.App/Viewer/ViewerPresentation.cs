using System.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal enum ViewerPresentationMode
{
    Auto,
    Raw,
}

internal enum ViewerColumnAlignment
{
    Left,
    Center,
    Right,
}

internal sealed record PresentedSourceSpan(
    int SourceStart,
    int SourceLength,
    int PresentedStart,
    int PresentedLength);

internal sealed record PresentedLine(
    ScannedLine Source,
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans)
{
    public static PresentedLine Raw(ScannedLine source) =>
        new(
            source,
            source.Text,
            source.Text.Length == 0
                ? []
                : [new PresentedSourceSpan(0, source.Text.Length, 0, source.Text.Length)]);

    public bool TryMapSourceRange(
        int sourceStart,
        int sourceLength,
        out int presentedStart,
        out int presentedLength)
    {
        presentedStart = 0;
        presentedLength = 0;
        if (sourceLength <= 0)
            return false;

        int sourceEnd = sourceStart + sourceLength;
        foreach (var span in SourceSpans)
        {
            if (span.SourceLength != span.PresentedLength ||
                sourceStart < span.SourceStart ||
                sourceEnd > span.SourceStart + span.SourceLength)
            {
                continue;
            }

            presentedStart = span.PresentedStart + sourceStart - span.SourceStart;
            presentedLength = sourceLength;
            return true;
        }

        return false;
    }
}

internal sealed record ViewerPresentationContext(
    IReadOnlyList<ScannedLine> VisibleLines,
    IReadOnlyList<ScannedLine> BeforeLines,
    IReadOnlyList<ScannedLine> AfterLines,
    int ViewportWidth)
{
    public IReadOnlyList<ScannedLine> AllLines
    {
        get
        {
            var result = new List<ScannedLine>(BeforeLines.Count + VisibleLines.Count + AfterLines.Count);
            result.AddRange(BeforeLines);
            result.AddRange(VisibleLines);
            result.AddRange(AfterLines);
            return result;
        }
    }
}

internal interface IViewerPresentationProvider
{
    IReadOnlyList<PresentedLine> Present(ViewerPresentationContext context);

    void Reset();
}

internal static class ViewerPresentationRegistry
{
    public static IViewerPresentationProvider? Create(string sourcePath)
    {
        string path = sourcePath ?? string.Empty;
        int suffixStart = path.IndexOfAny(['?', '#']);
        if (suffixStart >= 0)
            path = path[..suffixStart];

        return path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
            ? new MarkdownViewerPresentationProvider()
            : null;
    }
}

internal sealed class ViewerPresentationSession
{
    internal const int MaxContextLines = 64;
    internal const int MaxContextBytes = 256 * 1024;
    internal const int MaxContextLineBytes = 16 * 1024;

    private string? _sourcePath;
    private IViewerPresentationProvider? _provider;
    private bool _providerFailed;

    public IReadOnlyList<PresentedLine> Present(
        ViewerPresentationMode mode,
        string sourcePath,
        LineScanner scanner,
        IReadOnlyList<ScannedLine> visibleLines,
        int viewportWidth)
    {
        if (visibleLines.Count == 0 || mode == ViewerPresentationMode.Raw)
            return Raw(visibleLines);

        EnsureProvider(sourcePath);
        if (_provider is null || _providerFailed)
            return Raw(visibleLines);

        try
        {
            IReadOnlyList<ScannedLine> before = ReadBoundedContextBefore(scanner, visibleLines[0].StartOffset);
            var context = new ViewerPresentationContext(visibleLines, before, [], viewportWidth);
            var presented = _provider.Present(context);
            return presented.Count == visibleLines.Count ? presented : Raw(visibleLines);
        }
        catch
        {
            _providerFailed = true;
            return Raw(visibleLines);
        }
    }

    public void Reset()
    {
        _provider?.Reset();
        _providerFailed = false;
    }

    private void EnsureProvider(string sourcePath)
    {
        if (string.Equals(_sourcePath, sourcePath, StringComparison.Ordinal))
            return;

        _sourcePath = sourcePath;
        _provider = ViewerPresentationRegistry.Create(sourcePath);
        _providerFailed = false;
    }

    private static IReadOnlyList<ScannedLine> ReadBoundedContextBefore(LineScanner scanner, long firstVisibleOffset)
    {
        if (firstVisibleOffset <= scanner.ContentStartOffset)
            return [];

        var reversed = new List<ScannedLine>(MaxContextLines);
        long current = firstVisibleOffset;
        int inspectedBytes = 0;

        while (reversed.Count < MaxContextLines && inspectedBytes < MaxContextBytes)
        {
            int remaining = MaxContextBytes - inspectedBytes;
            long? previous = scanner
                .TryFindPreviousLineStartAsync(current, remaining)
                .GetAwaiter()
                .GetResult();
            if (previous is null || previous.Value >= current)
                break;

            long byteLength = current - previous.Value;
            if (byteLength > remaining)
                break;

            var scanned = scanner
                .ReadLinesAsync(previous.Value, 1, Math.Min(MaxContextLineBytes, remaining))
                .GetAwaiter()
                .GetResult();
            if (scanned.Lines.Count == 0 || scanned.Lines[0].NextOffset > current)
                break;

            reversed.Add(scanned.Lines[0]);
            inspectedBytes += checked((int)Math.Min(int.MaxValue, byteLength));
            current = previous.Value;
        }

        reversed.Reverse();
        return reversed;
    }

    private static IReadOnlyList<PresentedLine> Raw(IReadOnlyList<ScannedLine> source) =>
        source.Select(PresentedLine.Raw).ToArray();
}

internal sealed class MarkdownViewerPresentationProvider : IViewerPresentationProvider
{
    internal const int MaxColumnWidthCells = 4096;
    internal const int MaxPresentedLineChars = 64 * 1024;
    internal const int MaxCachedTables = 32;

    private readonly LinkedList<MarkdownTableLayout> _cache = new();

    public IReadOnlyList<PresentedLine> Present(ViewerPresentationContext context)
    {
        var all = context.AllLines;
        DiscoverTables(all);
        ExtendCachedTables(all);

        var result = new PresentedLine[context.VisibleLines.Count];
        for (int i = 0; i < context.VisibleLines.Count; i++)
        {
            ScannedLine source = context.VisibleLines[i];
            result[i] = TryPresentLine(source, out var presented)
                ? presented
                : PresentedLine.Raw(source);
        }

        return result;
    }

    public void Reset() => _cache.Clear();

    private void DiscoverTables(IReadOnlyList<ScannedLine> lines)
    {
        for (int separatorIndex = 1; separatorIndex < lines.Count; separatorIndex++)
        {
            string headerText = lines[separatorIndex - 1].Text;
            string separatorText = lines[separatorIndex].Text;
            if (!TryParseRow(headerText, out var header) ||
                !TryParseSeparator(separatorText, out var alignments) ||
                header.Count != alignments.Count ||
                header.Count == 0 ||
                (header.Count == 1 && !HasTablePipe(headerText) && !HasTablePipe(separatorText)))
            {
                continue;
            }

            long headerOffset = lines[separatorIndex - 1].StartOffset;
            var layout = FindByHeader(headerOffset) ?? new MarkdownTableLayout(
                headerOffset,
                lines[separatorIndex].StartOffset,
                alignments,
                new int[header.Count]);

            if (!TryUpdateWidths(layout, header))
                continue;

            layout.LastKnownNextOffset = lines[separatorIndex].NextOffset;
            int index = separatorIndex + 1;
            while (index < lines.Count && TryParseBodyRow(lines[index], layout, updateWidths: true))
            {
                layout.LastKnownNextOffset = lines[index].NextOffset;
                index++;
            }

            Touch(layout);
        }
    }

    private void ExtendCachedTables(IReadOnlyList<ScannedLine> lines)
    {
        foreach (var layout in _cache.ToArray())
        {
            bool overlapsKnownRange = lines.Any(line =>
                line.StartOffset >= layout.HeaderOffset && line.StartOffset < layout.LastKnownNextOffset);
            if (!overlapsKnownRange)
                continue;

            int index = 0;
            while (index < lines.Count && lines[index].StartOffset < layout.LastKnownNextOffset)
                index++;

            while (index < lines.Count &&
                   lines[index].StartOffset == layout.LastKnownNextOffset &&
                   TryParseBodyRow(lines[index], layout, updateWidths: true))
            {
                layout.LastKnownNextOffset = lines[index].NextOffset;
                index++;
            }
        }
    }

    private bool TryPresentLine(ScannedLine source, out PresentedLine presented)
    {
        presented = PresentedLine.Raw(source);
        var layout = FindForOffset(source.StartOffset);
        if (layout is null)
            return false;

        if (source.StartOffset == layout.SeparatorOffset)
        {
            string separator = RenderSeparator(layout.Widths);
            if (separator.Length > MaxPresentedLineChars)
                return false;

            presented = new PresentedLine(source, separator, []);
            return true;
        }

        if (!TryParseRow(source.Text, out var cells) || cells.Count > layout.ColumnCount)
            return false;

        while (cells.Count < layout.ColumnCount)
            cells.Add(MarkdownCell.EmptyAt(source.Text.Length));

        if (!TryRenderRow(source, cells, layout, out presented))
            return false;

        Touch(layout);
        return true;
    }

    private MarkdownTableLayout? FindForOffset(long offset)
    {
        foreach (var layout in _cache)
        {
            if (offset >= layout.HeaderOffset && offset < layout.LastKnownNextOffset)
                return layout;
        }

        return null;
    }

    private MarkdownTableLayout? FindByHeader(long headerOffset)
    {
        foreach (var layout in _cache)
        {
            if (layout.HeaderOffset == headerOffset)
                return layout;
        }

        return null;
    }

    private void Touch(MarkdownTableLayout layout)
    {
        LinkedListNode<MarkdownTableLayout>? existing = _cache.Find(layout);
        if (existing is not null)
            _cache.Remove(existing);

        _cache.AddFirst(layout);
        while (_cache.Count > MaxCachedTables)
            _cache.RemoveLast();
    }

    private static bool TryParseBodyRow(
        ScannedLine line,
        MarkdownTableLayout layout,
        bool updateWidths)
    {
        if (!HasTablePipe(line.Text) ||
            !TryParseRow(line.Text, out var cells) ||
            cells.Count > layout.ColumnCount)
        {
            return false;
        }

        while (cells.Count < layout.ColumnCount)
            cells.Add(MarkdownCell.EmptyAt(line.Text.Length));

        return !updateWidths || TryUpdateWidths(layout, cells);
    }

    private static bool TryUpdateWidths(MarkdownTableLayout layout, IReadOnlyList<MarkdownCell> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            int width = CellWidth(cells[i].Text);
            if (width > MaxColumnWidthCells)
                return false;

            layout.Widths[i] = Math.Max(layout.Widths[i], width);
        }

        long totalWidth = 1L + layout.Widths.Sum(width => width + 3L);
        return totalWidth <= MaxPresentedLineChars;
    }

    private static bool TryRenderRow(
        ScannedLine source,
        IReadOnlyList<MarkdownCell> cells,
        MarkdownTableLayout layout,
        out PresentedLine presented)
    {
        var text = new StringBuilder();
        var spans = new List<PresentedSourceSpan>();
        text.Append('│');

        for (int i = 0; i < layout.ColumnCount; i++)
        {
            MarkdownCell cell = cells[i];
            int contentWidth = CellWidth(cell.Text);
            if (contentWidth > layout.Widths[i])
            {
                presented = PresentedLine.Raw(source);
                return false;
            }

            int padding = layout.Widths[i] - contentWidth;
            int leftPadding = layout.Alignments[i] switch
            {
                ViewerColumnAlignment.Right => padding,
                ViewerColumnAlignment.Center => padding / 2,
                _ => 0,
            };
            int rightPadding = padding - leftPadding;

            text.Append(' ');
            text.Append(' ', leftPadding);
            int presentedStart = text.Length;
            text.Append(cell.Text);
            if (cell.SourceLength > 0)
            {
                spans.Add(new PresentedSourceSpan(
                    cell.SourceStart,
                    cell.SourceLength,
                    presentedStart,
                    cell.Text.Length));
            }

            text.Append(' ', rightPadding);
            text.Append(' ');
            text.Append(i == layout.ColumnCount - 1 ? '│' : '│');

            if (text.Length > MaxPresentedLineChars)
            {
                presented = PresentedLine.Raw(source);
                return false;
            }
        }

        presented = new PresentedLine(source, text.ToString(), spans);
        return true;
    }

    private static string RenderSeparator(IReadOnlyList<int> widths)
    {
        var text = new StringBuilder();
        text.Append('├');
        for (int i = 0; i < widths.Count; i++)
        {
            text.Append('─', widths[i] + 2);
            text.Append(i == widths.Count - 1 ? '┤' : '┼');
        }

        return text.ToString();
    }

    private static int CellWidth(string text) =>
        ConsoleTextMetrics.GetCellWidth(LargeFileViewer.SanitizeTextForConsole(text));

    internal static bool TryParseSeparator(
        string source,
        out IReadOnlyList<ViewerColumnAlignment> alignments)
    {
        alignments = [];
        if (!TryParseRow(source, out var cells) || cells.Count == 0)
            return false;

        var result = new ViewerColumnAlignment[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            string text = cells[i].Text.Trim();
            bool leftColon = text.StartsWith(':');
            bool rightColon = text.EndsWith(':');
            int start = leftColon ? 1 : 0;
            int length = text.Length - start - (rightColon ? 1 : 0);
            if (length < 3)
                return false;

            for (int j = start; j < start + length; j++)
            {
                if (text[j] != '-')
                    return false;
            }

            result[i] = leftColon && rightColon
                ? ViewerColumnAlignment.Center
                : rightColon
                    ? ViewerColumnAlignment.Right
                    : ViewerColumnAlignment.Left;
        }

        alignments = result;
        return true;
    }

    internal static bool TryParseRow(string source, out List<MarkdownCell> cells)
    {
        cells = [];
        if (source.Length > MaxPresentedLineChars)
            return false;

        int contentStart = 0;
        while (contentStart < source.Length && char.IsWhiteSpace(source[contentStart]))
            contentStart++;
        int contentEnd = source.Length;
        while (contentEnd > contentStart && char.IsWhiteSpace(source[contentEnd - 1]))
            contentEnd--;

        var separators = FindPipeSeparators(source, contentStart, contentEnd);
        bool leadingPipe = separators.Count > 0 && separators[0] == contentStart;
        bool trailingPipe = separators.Count > 0 && separators[^1] == contentEnd - 1;

        int cellStart = leadingPipe ? contentStart + 1 : contentStart;
        int separatorStart = leadingPipe ? 1 : 0;
        int separatorEnd = separators.Count - (trailingPipe ? 1 : 0);

        for (int i = separatorStart; i < separatorEnd; i++)
        {
            int pipe = separators[i];
            cells.Add(CreateCell(source, cellStart, pipe));
            cellStart = pipe + 1;
        }

        int finalEnd = trailingPipe ? contentEnd - 1 : contentEnd;
        cells.Add(CreateCell(source, cellStart, finalEnd));
        return true;
    }

    private static bool HasTablePipe(string source)
    {
        int start = 0;
        while (start < source.Length && char.IsWhiteSpace(source[start]))
            start++;
        int end = source.Length;
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;
        return FindPipeSeparators(source, start, end).Count > 0;
    }

    private static List<int> FindPipeSeparators(string source, int start, int end)
    {
        var result = new List<int>();
        int codeTicks = 0;

        for (int i = start; i < end;)
        {
            if (source[i] == '`')
            {
                int ticks = 1;
                while (i + ticks < end && source[i + ticks] == '`')
                    ticks++;

                if (codeTicks == 0)
                    codeTicks = ticks;
                else if (ticks == codeTicks)
                    codeTicks = 0;

                i += ticks;
                continue;
            }

            if (source[i] == '|' && codeTicks == 0 && !IsEscaped(source, i))
                result.Add(i);

            i++;
        }

        return result;
    }

    private static bool IsEscaped(string source, int index)
    {
        int slashCount = 0;
        for (int i = index - 1; i >= 0 && source[i] == '\\'; i--)
            slashCount++;
        return slashCount % 2 != 0;
    }

    private static MarkdownCell CreateCell(string source, int start, int end)
    {
        start = Math.Clamp(start, 0, source.Length);
        end = Math.Clamp(end, start, source.Length);
        while (start < end && char.IsWhiteSpace(source[start]))
            start++;
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;

        return new MarkdownCell(start, end - start, source[start..end]);
    }

    internal sealed record MarkdownCell(int SourceStart, int SourceLength, string Text)
    {
        public static MarkdownCell EmptyAt(int sourceIndex) => new(sourceIndex, 0, string.Empty);
    }

    private sealed class MarkdownTableLayout(
        long headerOffset,
        long separatorOffset,
        IReadOnlyList<ViewerColumnAlignment> alignments,
        int[] widths)
    {
        public long HeaderOffset { get; } = headerOffset;
        public long SeparatorOffset { get; } = separatorOffset;
        public IReadOnlyList<ViewerColumnAlignment> Alignments { get; } = alignments;
        public int[] Widths { get; } = widths;
        public int ColumnCount => Widths.Length;
        public long LastKnownNextOffset { get; set; }
    }
}
