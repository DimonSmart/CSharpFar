from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def read_normalized(relative: str):
    path = ROOT / relative
    raw = path.read_bytes()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    text = raw.decode("utf-8")
    newline = "\r\n" if "\r\n" in text else "\n"
    return path, text.replace("\r\n", "\n"), newline, bom

def write_normalized(relative: str, text: str, newline: str = "\n", bom: bool = False):
    path = ROOT / relative
    data = text.replace("\n", newline).encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    path.write_bytes(data)

def replace_once(relative: str, old: str, new: str):
    path, text, newline, bom = read_normalized(relative)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{relative}: expected one occurrence, found {count}: {old[:100]!r}")
    text = text.replace(old, new, 1)
    write_normalized(relative, text, newline, bom)

def write_new(relative: str, text: str):
    path = ROOT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        raise RuntimeError(f"{relative}: file already exists")
    path.write_text(text, encoding="utf-8", newline="\n")

write_new("src/CSharpFar.Core/Abstractions/IUriLauncher.cs", """namespace CSharpFar.Core.Abstractions;

public interface IUriLauncher
{
    void Open(Uri uri);
}
""")

write_new("src/CSharpFar.Shell/SystemUriLauncher.cs", """using System.Diagnostics;
using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class SystemUriLauncher : IUriLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;

    public SystemUriLauncher()
        : this(Process.Start)
    {
    }

    internal SystemUriLauncher(Func<ProcessStartInfo, Process?> startProcess)
    {
        _startProcess = startProcess;
    }

    public void Open(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));

        var startInfo = new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        };

        using Process? process = _startProcess(startInfo);
        if (process is null)
            throw new InvalidOperationException($"Failed to open URI: {uri}");
    }
}
""")

write_new("src/CSharpFar.App/Viewer/MarkdownLinkTargetResolver.cs", """namespace CSharpFar.App.Viewer;

internal enum MarkdownLinkTargetKind
{
    Unsupported,
    ExternalUri,
    RelativeFile,
}

internal sealed record MarkdownLinkTarget(
    MarkdownLinkTargetKind Kind,
    Uri? Uri = null,
    string? FilePath = null)
{
    public static MarkdownLinkTarget Unsupported { get; } = new(MarkdownLinkTargetKind.Unsupported);
}

internal static class MarkdownLinkTargetResolver
{
    public static MarkdownLinkTarget Resolve(string? target, string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(target) || target.StartsWith('#'))
            return MarkdownLinkTarget.Unsupported;

        if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return new MarkdownLinkTarget(MarkdownLinkTargetKind.ExternalUri, Uri: uri);
            }

            return MarkdownLinkTarget.Unsupported;
        }

        if (LooksLikeUriScheme(target) ||
            target.Contains('?') ||
            target.Contains('#') ||
            IsRootedTarget(target))
        {
            return MarkdownLinkTarget.Unsupported;
        }

        if (string.IsNullOrWhiteSpace(sourceFilePath) || !Path.IsPathRooted(sourceFilePath))
            return MarkdownLinkTarget.Unsupported;

        try
        {
            string fullSourcePath = Path.GetFullPath(sourceFilePath);
            string? baseDirectory = Path.GetDirectoryName(fullSourcePath);
            if (string.IsNullOrEmpty(baseDirectory))
                return MarkdownLinkTarget.Unsupported;

            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, target));
            if (Directory.Exists(fullPath))
                return MarkdownLinkTarget.Unsupported;

            return new MarkdownLinkTarget(MarkdownLinkTargetKind.RelativeFile, FilePath: fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return MarkdownLinkTarget.Unsupported;
        }
    }

    private static bool LooksLikeUriScheme(string target)
    {
        int colon = target.IndexOf(':');
        if (colon <= 0 || !char.IsAsciiLetter(target[0]))
            return false;

        for (int i = 1; i < colon; i++)
        {
            char ch = target[i];
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not '+' and not '-' and not '.')
                return false;
        }

        return true;
    }

    private static bool IsRootedTarget(string target) =>
        Path.IsPathRooted(target) ||
        target.StartsWith('/') ||
        target.StartsWith('\\') ||
        (target.Length >= 2 && char.IsAsciiLetter(target[0]) && target[1] == ':');
}
""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """internal sealed record PresentedStyleSpan(
    int Start,
    int Length,
    ViewerTextStyle Style);

internal sealed record PresentedLine(
    ScannedLine Source,
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans)
{
    public PresentedLine(
        ScannedLine source,
        string text,
        IReadOnlyList<PresentedSourceSpan> sourceSpans)
        : this(source, text, sourceSpans, [])
    {
    }
""",
    """internal sealed record PresentedStyleSpan(
    int Start,
    int Length,
    ViewerTextStyle Style);

internal sealed record PresentedLinkSpan(
    int Start,
    int Length,
    string Target);

internal sealed record PresentedLine(
    ScannedLine Source,
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans,
    IReadOnlyList<PresentedLinkSpan> LinkSpans)
{
    public PresentedLine(
        ScannedLine source,
        string text,
        IReadOnlyList<PresentedSourceSpan> sourceSpans)
        : this(source, text, sourceSpans, [], [])
    {
    }

    public PresentedLine(
        ScannedLine source,
        string text,
        IReadOnlyList<PresentedSourceSpan> sourceSpans,
        IReadOnlyList<PresentedStyleSpan> styleSpans)
        : this(source, text, sourceSpans, styleSpans, [])
    {
    }
""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """internal sealed record MarkdownInlineTransform(
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans,
    bool Changed);""",
    """internal sealed record MarkdownInlineTransform(
    string Text,
    IReadOnlyList<PresentedSourceSpan> SourceSpans,
    IReadOnlyList<PresentedStyleSpan> StyleSpans,
    IReadOnlyList<PresentedLinkSpan> LinkSpans,
    bool Changed);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        int rawStart = 0;""",
    """        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        var linkSpans = new List<PresentedLinkSpan>();
        int rawStart = 0;""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """            styleSpans.Add(new PresentedStyleSpan(presentedStart, construct.ContentLength, construct.Style));

            changed = true;""",
    """            styleSpans.Add(new PresentedStyleSpan(presentedStart, construct.ContentLength, construct.Style));
            if (construct.LinkTarget is not null)
            {
                linkSpans.Add(new PresentedLinkSpan(
                    presentedStart,
                    construct.ContentLength,
                    construct.LinkTarget));
            }

            changed = true;""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """        return new MarkdownInlineTransform(presentedText, sourceSpans, styleSpans, true);""",
    """        return new MarkdownInlineTransform(presentedText, sourceSpans, styleSpans, linkSpans, true);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """            [],
            false);""",
    """            [],
            [],
            false);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """            ViewerTextStyle.InlineCode);""",
    """            ViewerTextStyle.InlineCode,
            null);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """                    delimiterLength == 2 ? ViewerTextStyle.Bold : ViewerTextStyle.Italic);""",
    """                    delimiterLength == 2 ? ViewerTextStyle.Bold : ViewerTextStyle.Italic,
                    null);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """        construct = new InlineConstruct(
            labelStart,
            closeBracket - labelStart,
            closeParenthesis + 1,
            ViewerTextStyle.Link);""",
    """        construct = new InlineConstruct(
            labelStart,
            closeBracket - labelStart,
            closeParenthesis + 1,
            ViewerTextStyle.Link,
            source[destinationStart..closeParenthesis]);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownInlinePresentation.cs",
    """    private readonly record struct InlineConstruct(
        int ContentStart,
        int ContentLength,
        int EndExclusive,
        ViewerTextStyle Style);""",
    """    private readonly record struct InlineConstruct(
        int ContentStart,
        int ContentLength,
        int EndExclusive,
        ViewerTextStyle Style,
        string? LinkTarget);""")

replace_once(
    "src/CSharpFar.App/Viewer/MarkdownHeadingPresentation.cs",
    """        presented = new PresentedLine(source, inline.Text, inline.SourceSpans, styles);""",
    """        presented = new PresentedLine(source, inline.Text, inline.SourceSpans, styles, inline.LinkSpans);""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """            presented = new PresentedLine(source, inline.Text, inline.SourceSpans, inline.StyleSpans);
            return true;""",
    """            presented = new PresentedLine(
                source,
                inline.Text,
                inline.SourceSpans,
                inline.StyleSpans,
                inline.LinkSpans);
            return true;""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        text.Append('│');""",
    """        var sourceSpans = new List<PresentedSourceSpan>();
        var styleSpans = new List<PresentedStyleSpan>();
        var linkSpans = new List<PresentedLinkSpan>();
        text.Append('│');""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """            foreach (PresentedStyleSpan span in inline.StyleSpans)
            {
                styleSpans.Add(span with
                {
                    Start = span.Start + presentedStart,
                });
            }

            text.Append(' ', rightPadding);""",
    """            foreach (PresentedStyleSpan span in inline.StyleSpans)
            {
                styleSpans.Add(span with
                {
                    Start = span.Start + presentedStart,
                });
            }

            foreach (PresentedLinkSpan span in inline.LinkSpans)
            {
                linkSpans.Add(span with
                {
                    Start = span.Start + presentedStart,
                });
            }

            text.Append(' ', rightPadding);""")

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """        presented = new PresentedLine(source, text.ToString(), sourceSpans, styleSpans);
        return true;""",
    """        presented = new PresentedLine(source, text.ToString(), sourceSpans, styleSpans, linkSpans);
        return true;""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewerOptions.cs",
    """    public ITextClipboard? Clipboard { get; init; }

    public bool HasSiblingFiles =>""",
    """    public ITextClipboard? Clipboard { get; init; }
    public IFileLauncher? FileLauncher { get; init; }
    public IUriLauncher? UriLauncher { get; init; }

    public bool HasSiblingFiles =>""")

replace_once(
    "src/CSharpFar.App/Bootstrap/ApplicationServicesBuilder.cs",
    """        IProcessesAndPortsPlatformService? processesAndPorts = null,
        IFileUsagePlatformService? fileUsage = null)
    {""",
    """        IProcessesAndPortsPlatformService? processesAndPorts = null,
        IFileUsagePlatformService? fileUsage = null,
        IUriLauncher? uriLauncher = null)
    {""")

replace_once(
    "src/CSharpFar.App/Bootstrap/ApplicationServicesBuilder.cs",
    """        var effectiveFileLauncher = core.FileLauncher;
        var effectiveClipboard = core.Clipboard;""",
    """        var effectiveFileLauncher = core.FileLauncher;
        var effectiveUriLauncher = uriLauncher ?? new SystemUriLauncher();
        var effectiveClipboard = core.Clipboard;""")

replace_once(
    "src/CSharpFar.App/Bootstrap/ApplicationServicesBuilder.cs",
    """            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            (state, rows) => callbacks.SafeRefresh(state, rows));""",
    """            state => callbacks.PanelSideForState(state),
            side => callbacks.VisibleRowsForSide(side),
            (state, rows) => callbacks.SafeRefresh(state, rows),
            effectiveFileLauncher,
            effectiveUriLauncher);""")

replace_once(
    "src/CSharpFar.App/Files/PanelFileViewerService.cs",
    """    private readonly Func<FilePanelState, PanelSide?> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRowsForSide;
    private readonly Action<FilePanelState, int> _safeRefresh;""",
    """    private readonly Func<FilePanelState, PanelSide?> _panelSideForState;
    private readonly Func<PanelSide, int> _visibleRowsForSide;
    private readonly Action<FilePanelState, int> _safeRefresh;
    private readonly IFileLauncher? _fileLauncher;
    private readonly IUriLauncher? _uriLauncher;""")

replace_once(
    "src/CSharpFar.App/Files/PanelFileViewerService.cs",
    """        Func<FilePanelState, PanelSide?> panelSideForState,
        Func<PanelSide, int> visibleRowsForSide,
        Action<FilePanelState, int> safeRefresh)""",
    """        Func<FilePanelState, PanelSide?> panelSideForState,
        Func<PanelSide, int> visibleRowsForSide,
        Action<FilePanelState, int> safeRefresh,
        IFileLauncher? fileLauncher = null,
        IUriLauncher? uriLauncher = null)""")

replace_once(
    "src/CSharpFar.App/Files/PanelFileViewerService.cs",
    """        _panelSideForState = panelSideForState;
        _visibleRowsForSide = visibleRowsForSide;
        _safeRefresh = safeRefresh;""",
    """        _panelSideForState = panelSideForState;
        _visibleRowsForSide = visibleRowsForSide;
        _safeRefresh = safeRefresh;
        _fileLauncher = fileLauncher;
        _uriLauncher = uriLauncher;""")

replace_once(
    "src/CSharpFar.App/Files/PanelFileViewerService.cs",
    """                new LargeFileViewerOptions
                {
                    Clipboard = _clipboard,
                    EditCurrentFile = canWrite
                        ? () => EditRemoteFile(state, item)
                        : null,
                });""",
    """                new LargeFileViewerOptions
                {
                    Clipboard = _clipboard,
                    FileLauncher = _fileLauncher,
                    UriLauncher = _uriLauncher,
                    EditCurrentFile = canWrite
                        ? () => EditRemoteFile(state, item)
                        : null,
                });""")

replace_once(
    "src/CSharpFar.App/Files/PanelFileViewerService.cs",
    """        return new LargeFileViewerOptions
        {
            FilePaths = siblings,
            CurrentFileIndex = index,
            CurrentFileChanged = path => RecordViewedFile(state, path),
            EditFile = EditLocalFile,
            Clipboard = _clipboard,
        };""",
    """        return new LargeFileViewerOptions
        {
            FilePaths = siblings,
            CurrentFileIndex = index,
            CurrentFileChanged = path => RecordViewedFile(state, path),
            EditFile = EditLocalFile,
            Clipboard = _clipboard,
            FileLauncher = _fileLauncher,
            UriLauncher = _uriLauncher,
        };""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """                    return HandleViewerInput(filePath, reader, state, options, routed.Frame, input);""",
    """                    return HandleViewerInput(
                        filePath,
                        hasPhysicalSourcePath: true,
                        reader,
                        state,
                        options,
                        routed.Frame,
                        input);""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """                (routed, input) => HandleViewerInput(filePath, reader, state, options, routed.Frame, input),""",
    """                (routed, input) => HandleViewerInput(
                    filePath,
                    hasPhysicalSourcePath: false,
                    reader,
                    state,
                    options,
                    routed.Frame,
                    input),""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """    private ModalDialogLoopResult<ViewerLoopAction> HandleViewerInput(
        string filePath,
        IFileByteReader reader,
        LargeFileViewerState state,""",
    """    private ModalDialogLoopResult<ViewerLoopAction> HandleViewerInput(
        string filePath,
        bool hasPhysicalSourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        if (input.ScrollLines is int scrollLines)
        {
            ApplyScrollLines(reader, state, frame.View, scrollLines);
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueChanged;
        }

        if (input.Key is not ConsoleKeyInfo key)""",
    """        if (input.ScrollLines is int scrollLines)
        {
            ApplyScrollLines(reader, state, frame.View, scrollLines);
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueChanged;
        }

        if (input.LinkTarget is string linkTarget)
        {
            OpenMarkdownLink(
                linkTarget,
                hasPhysicalSourcePath ? filePath : null,
                options);
            return ModalDialogLoopResult<ViewerLoopAction>.ContinueNoChange;
        }

        if (input.Key is not ConsoleKeyInfo key)""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        var presented = state.Presentation.Present(
            state.PresentationMode,
            sourcePath,
            state.LineScanner,
            scanned.Lines,
            width);

        for (int row = 0; row < contentHeight; row++)""",
    """        var presented = state.Presentation.Present(
            state.PresentationMode,
            sourcePath,
            state.LineScanner,
            scanned.Lines,
            width);
        var linkHits = new List<ViewerLinkHit>();

        for (int row = 0; row < contentHeight; row++)""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """                    width,
                    state.SearchMatch,
                    segmentStartIndex: 0);""",
    """                    width,
                    state.SearchMatch,
                    segmentStartIndex: 0,
                    linkHits);""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        return new LargeFileRenderView(scanned.Lines, scanned.NextOffset);
    }

    private LargeFileRenderView DrawWrappedTextContent(""",
    """        return new LargeFileRenderView(scanned.Lines, scanned.NextOffset, linkHits);
    }

    private LargeFileRenderView DrawWrappedTextContent(""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        var lines = new List<ScannedLine>();
        int row = 0;""",
    """        var lines = new List<ScannedLine>();
        var linkHits = new List<ViewerLinkHit>();
        int row = 0;""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """                    width,
                    state.SearchMatch,
                    segment.StartIndex);
                row++;""",
    """                    width,
                    state.SearchMatch,
                    segment.StartIndex,
                    linkHits);
                row++;""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        return new LargeFileRenderView(lines, nextOffset);
    }

    private LargeFileRenderView DrawBinaryContent(""",
    """        return new LargeFileRenderView(lines, nextOffset, linkHits);
    }

    private LargeFileRenderView DrawBinaryContent(""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        return new LargeFileRenderView(rows, nextOffset);
    }

    private static bool IsHexMatchOnRow""",
    """        return new LargeFileRenderView(rows, nextOffset, []);
    }

    private static bool IsHexMatchOnRow""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        int width,
        ViewerSearchMatch? match,
        int segmentStartIndex)
    {""",
    """        int width,
        ViewerSearchMatch? match,
        int segmentStartIndex,
        List<ViewerLinkHit> linkHits)
    {""")

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """        canvas.WriteForced(0, y, visible, CSharpFarPaletteStyles.CommandLine(_palette));
        ApplyMarkdownStyles(canvas, presented, line, y, scrollLeft, width, segmentStartIndex, layout);
        if (match is not { IsHex: false } ||""",
    """        canvas.WriteForced(0, y, visible, CSharpFarPaletteStyles.CommandLine(_palette));
        ApplyMarkdownStyles(canvas, presented, line, y, scrollLeft, width, segmentStartIndex, layout);
        CollectLinkHits(presented, line, y, scrollLeft, width, segmentStartIndex, layout, linkHits);
        if (match is not { IsHex: false } ||""")

marker = """    private void ApplyMarkdownStyles(
"""
insert = """    private static void CollectLinkHits(
        PresentedLine presented,
        string line,
        int y,
        int scrollLeft,
        int width,
        int segmentStartIndex,
        ViewerTextLayout layout,
        List<ViewerLinkHit> linkHits)
    {
        if (presented.LinkSpans.Count == 0)
            return;

        int segmentEndIndex = segmentStartIndex + line.Length;
        int visibleStart = scrollLeft;
        int visibleEnd = scrollLeft + width;

        foreach (PresentedLinkSpan span in presented.LinkSpans)
        {
            int spanEnd = span.Start + span.Length;
            if (spanEnd <= segmentStartIndex || span.Start >= segmentEndIndex)
                continue;

            int localStart = Math.Max(span.Start, segmentStartIndex) - segmentStartIndex;
            int localEnd = Math.Min(spanEnd, segmentEndIndex) - segmentStartIndex;
            int linkStartCell = layout.CellOffsetFromSourceIndex(localStart);
            int linkEndCell = layout.CellOffsetFromSourceIndex(localEnd);
            int hitStart = Math.Max(linkStartCell, visibleStart);
            int hitEnd = Math.Min(linkEndCell, visibleEnd);
            if (hitEnd <= hitStart)
                continue;

            linkHits.Add(new ViewerLinkHit(
                new Rect(hitStart - visibleStart, y, hitEnd - hitStart, 1),
                span.Target));
        }
    }

"""
path, text, nl, bom = read_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs")
if text.count(marker) != 1:
    raise RuntimeError("LargeFileViewer.cs: ApplyMarkdownStyles marker mismatch")
text = text.replace(marker, insert + marker, 1)
write_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs", text, nl, bom)

marker = """    private void ShowUnsupported(string command) =>
"""
insert = """    private void OpenMarkdownLink(
        string target,
        string? sourceFilePath,
        LargeFileViewerOptions options)
    {
        MarkdownLinkTarget resolved = MarkdownLinkTargetResolver.Resolve(target, sourceFilePath);
        try
        {
            switch (resolved.Kind)
            {
                case MarkdownLinkTargetKind.ExternalUri when
                    resolved.Uri is not null &&
                    options.UriLauncher is not null:
                    options.UriLauncher.Open(resolved.Uri);
                    break;

                case MarkdownLinkTargetKind.RelativeFile when
                    resolved.FilePath is not null &&
                    options.FileLauncher is not null:
                    string workingDirectory = Path.GetDirectoryName(resolved.FilePath) ?? string.Empty;
                    options.FileLauncher.OpenFile(resolved.FilePath, workingDirectory);
                    break;
            }
        }
        catch (Exception ex)
        {
            _dialogs.Message("Viewer", ex.Message);
        }
    }

"""
path, text, nl, bom = read_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs")
if text.count(marker) != 1:
    raise RuntimeError("LargeFileViewer.cs: ShowUnsupported marker mismatch")
text = text.replace(marker, insert + marker, 1)
write_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs", text, nl, bom)

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """            if (context.Target == Content && mouse.Kind == MouseEventKind.Wheel)
            {
                const int wheelLines = 3;
                return mouse.Button switch
                {
                    MouseButton.WheelUp => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(-wheelLines)),
                    MouseButton.WheelDown => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(wheelLines)),
                    _ => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None),
                };
            }

            if (FunctionKeysController.TryGetAction(""",
    """            if (context.Target == Content && mouse.Kind == MouseEventKind.Wheel)
            {
                const int wheelLines = 3;
                return mouse.Button switch
                {
                    MouseButton.WheelUp => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(-wheelLines)),
                    MouseButton.WheelDown => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromScroll(wheelLines)),
                    _ => new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.None),
                };
            }

            if (context.Target == Content &&
                TryGetLinkTarget(mouse, frame.View.LinkHits, out string linkTarget))
            {
                return new InteractiveSurfaceRouteResult<ViewerInput>(ViewerInput.FromLink(linkTarget));
            }

            if (FunctionKeysController.TryGetAction(""")

marker = """    private sealed class LargeFileViewerLayer : InteractiveSurfaceLayer<LargeFileViewerFrame, ViewerInput>
"""
insert = """    internal static bool TryGetLinkTarget(
        MouseConsoleInputEvent mouse,
        IReadOnlyList<ViewerLinkHit> linkHits,
        out string target)
    {
        target = string.Empty;
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind != MouseEventKind.Up ||
            mouse.Modifiers is not (MouseKeyModifiers.None or MouseKeyModifiers.Control))
        {
            return false;
        }

        foreach (ViewerLinkHit hit in linkHits)
        {
            Rect bounds = hit.Bounds;
            if (mouse.X < bounds.X ||
                mouse.X >= bounds.X + bounds.Width ||
                mouse.Y < bounds.Y ||
                mouse.Y >= bounds.Y + bounds.Height)
            {
                continue;
            }

            target = hit.Target;
            return true;
        }

        return false;
    }

"""
path, text, nl, bom = read_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs")
if text.count(marker) != 1:
    raise RuntimeError("LargeFileViewer.cs: layer marker mismatch")
text = text.replace(marker, insert + marker, 1)
write_normalized("src/CSharpFar.App/Viewer/LargeFileViewer.cs", text, nl, bom)

replace_once(
    "src/CSharpFar.App/Viewer/LargeFileViewer.cs",
    """    private readonly record struct ViewerInput(ConsoleKeyInfo? Key, int? ScrollLines)
    {
        public static ViewerInput None => new(null, null);

        public static ViewerInput FromKey(ConsoleKeyInfo key) => new(key, null);

        public static ViewerInput FromScroll(int lines) => new(null, lines);
    }

    private sealed record LargeFileRenderView(IReadOnlyList<ScannedLine> Lines, long NextOffset);

    private sealed record WrappedTextSegment(int StartIndex, string Text);""",
    """    private readonly record struct ViewerInput(ConsoleKeyInfo? Key, int? ScrollLines, string? LinkTarget)
    {
        public static ViewerInput None => new(null, null, null);

        public static ViewerInput FromKey(ConsoleKeyInfo key) => new(key, null, null);

        public static ViewerInput FromScroll(int lines) => new(null, lines, null);

        public static ViewerInput FromLink(string target) => new(null, null, target);
    }

    private sealed record LargeFileRenderView(
        IReadOnlyList<ScannedLine> Lines,
        long NextOffset,
        IReadOnlyList<ViewerLinkHit> LinkHits);

    internal sealed record ViewerLinkHit(Rect Bounds, string Target);

    private sealed record WrappedTextSegment(int StartIndex, string Text);""")

replace_once(
    ".idd/intent/0032.spec-unified-file-viewer.md",
    """- Full CommonMark parsing, recursive inline Markdown, interactive links, images, Setext
  headings, blockquotes, lists, document-wide block state, fenced-code rendering/suppression,
  or nested/composable semantic styles.""",
    """- Full CommonMark parsing, recursive inline Markdown, images, Setext headings, blockquotes,
  lists, document-wide block state, fenced-code rendering/suppression, or nested/composable
  semantic styles.
- Markdown anchors, non-HTTP URI schemes, rooted/network file links, local-link query/fragment
  navigation, hover/tooltip/status-preview behavior, and OSC-8-dependent interaction.""")

replace_once(
    ".idd/intent/0032.spec-unified-file-viewer.md",
    """- Update tests and viewer documentation.

## Non-goals""",
    """- Markdown link destinations are retained as semantic presentation spans independent of
  visual style spans. The viewer derives transient clickable terminal-cell regions from the
  same render layout used for text; wrapping may create multiple regions for one link and
  scrolling/clipping/resize always rebuild regions from the current frame.
- Left-button release with no modifier or Ctrl-only activates a visible Markdown link. HTTP
  and HTTPS targets use a URI-launch abstraction; relative file targets resolve only against
  a known physical Markdown source path and then use the existing file launcher. Unsupported
  or malformed URI-like targets never fall back to file paths.
- Update tests and viewer documentation.

## Non-goals""")

replace_once(
    ".idd/intent/0032.spec-unified-file-viewer.md",
    """- Heading processing is line-local; Setext headings, fenced-code-aware suppression, full
  CommonMark parsing, and document-wide Markdown state remain outside this scope.

## Verification""",
    """- Heading processing is line-local; Setext headings, fenced-code-aware suppression, full
  CommonMark parsing, and document-wide Markdown state remain outside this scope.
- Markdown link targets are preserved during presentation without reparsing rendered text.
  Table padding is not clickable; terminal-cell hit testing remains correct across horizontal
  scroll, wrapping, clipping, Unicode/wide characters, and current-frame resize/scroll state.
- Left click and Ctrl+left click on a visible link activate it on mouse-up. HTTP/HTTPS use the
  URI launcher; relative files use the file launcher after resolution against the physical
  source Markdown directory. Unsupported schemes, anchors, rooted/network paths, missing base
  paths, and local query/fragment targets do not launch.

## Verification""")

write_new("tests/CSharpFar.Tests/MarkdownLinkPresentationTests.cs", """using System.Text;
using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkPresentationTests
{
    [Fact]
    public void InlineLinkPreservesTargetAndVisibleLabelSpan()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "See [documentation](https://example.com/docs?q=test#section).");

        Assert.Equal("See documentation.", result.Text);
        PresentedLinkSpan link = Assert.Single(result.LinkSpans);
        Assert.Equal(new PresentedLinkSpan(
            4,
            "documentation".Length,
            "https://example.com/docs?q=test#section"), link);
    }

    [Fact]
    public void MultipleLinksHaveIndependentSemanticSpans()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "[one](https://one.example) and [two](docs/two.md)");

        Assert.Equal("one and two", result.Text);
        Assert.Equal(2, result.LinkSpans.Count);
        Assert.Equal([0, 8], result.LinkSpans.Select(span => span.Start));
        Assert.Equal(
            ["https://one.example", "docs/two.md"],
            result.LinkSpans.Select(span => span.Target));
    }

    [Fact]
    public void FormattingSyntaxInsideLabelDoesNotLoseLinkSemantics()
    {
        MarkdownInlineTransform result = MarkdownInlinePresentation.Transform(
            "[**Documentation**](https://example.com)");

        Assert.Equal("**Documentation**", result.Text);
        Assert.Equal(
            new PresentedLinkSpan(0, result.Text.Length, "https://example.com"),
            Assert.Single(result.LinkSpans));
    }

    [Fact]
    public void TableMovesLinkSpanWithCellPaddingButDoesNotIncludePadding()
    {
        ScannedLine[] lines = Lines(
            "| Name | Link |",
            "|---|---:|",
            "| Test | [site](https://example.com) |");
        var provider = new MarkdownViewerPresentationProvider();

        PresentedLine row = provider.Present(
            new ViewerPresentationContext(lines, [], [], 120))[2];

        PresentedLinkSpan link = Assert.Single(row.LinkSpans);
        Assert.Equal("site", row.Text.Substring(link.Start, link.Length));
        Assert.Equal("https://example.com", link.Target);
        Assert.NotEqual(' ', row.Text[link.Start]);
        Assert.NotEqual(' ', row.Text[link.Start + link.Length - 1]);
    }

    [Fact]
    public void RawLineHasNoSemanticLinks()
    {
        PresentedLine raw = PresentedLine.Raw(Line("[site](https://example.com)", 0));

        Assert.Empty(raw.LinkSpans);
    }

    private static ScannedLine Line(string text, long start) =>
        new(start, start + Encoding.UTF8.GetByteCount(text) + 1, text);

    private static ScannedLine[] Lines(params string[] texts)
    {
        var result = new ScannedLine[texts.Length];
        long offset = 0;
        for (int i = 0; i < texts.Length; i++)
        {
            result[i] = Line(texts[i], offset);
            offset = result[i].NextOffset;
        }

        return result;
    }
}
""")

write_new("tests/CSharpFar.Tests/MarkdownLinkTargetResolverTests.cs", """using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkTargetResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public MarkdownLinkTargetResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CSharpFarLinkTarget_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Theory]
    [InlineData("http://example.com/path?q=1#x")]
    [InlineData("HTTPS://example.com/path")]
    public void HttpTargetsAreExternalAndPreserveUri(string target)
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve(target, null);

        Assert.Equal(MarkdownLinkTargetKind.ExternalUri, result.Kind);
        Assert.Equal(target, result.Uri!.OriginalString);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void RelativeFileResolvesAgainstPhysicalMarkdownDirectory()
    {
        string docs = Path.Combine(_tempDirectory, "docs");
        Directory.CreateDirectory(docs);
        string source = Path.Combine(docs, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("../config.json", source);

        Assert.Equal(MarkdownLinkTargetKind.RelativeFile, result.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(_tempDirectory, "config.json")), result.FilePath);
    }

    [Theory]
    [InlineData("mailto:test@example.com")]
    [InlineData("file:readme.md")]
    [InlineData("ssh:host")]
    [InlineData("vscode:file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("scheme:broken target")]
    [InlineData("#section")]
    [InlineData("docs/readme.md#section")]
    [InlineData("docs/readme.md?x=1")]
    [InlineData("/usr/local/file.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    [InlineData("C:\\file.txt")]
    [InlineData("")]
    public void UnsupportedTargetsNeverBecomeFiles(string target)
    {
        string source = Path.Combine(_tempDirectory, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve(target, source);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
        Assert.Null(result.Uri);
        Assert.Null(result.FilePath);
    }

    [Fact]
    public void RelativeTargetWithoutPhysicalSourceDoesNotUseCurrentDirectory()
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs/readme.md", null);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }

    [Fact]
    public void RelativeSourcePathDoesNotUseCurrentDirectoryAsImplicitBase()
    {
        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs/readme.md", "README.md");

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }

    [Fact]
    public void DirectoryTargetIsUnsupported()
    {
        string targetDirectory = Path.Combine(_tempDirectory, "docs");
        Directory.CreateDirectory(targetDirectory);
        string source = Path.Combine(_tempDirectory, "readme.md");

        MarkdownLinkTarget result = MarkdownLinkTargetResolver.Resolve("docs", source);

        Assert.Equal(MarkdownLinkTargetKind.Unsupported, result.Kind);
    }
}
""")

write_new("tests/CSharpFar.Tests/MarkdownLinkMouseTests.cs", """using CSharpFar.App.Viewer;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkMouseTests
{
    private static readonly IReadOnlyList<LargeFileViewer.ViewerLinkHit> Hits =
    [
        new(new Rect(5, 2, 4, 1), "https://example.com"),
    ];

    [Theory]
    [InlineData(MouseKeyModifiers.None)]
    [InlineData(MouseKeyModifiers.Control)]
    public void LeftButtonUpActivatesLink(MouseKeyModifiers modifiers)
    {
        var mouse = new MouseConsoleInputEvent(
            6, 2, MouseButton.Left, MouseEventKind.Up, modifiers);

        Assert.True(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out string target));
        Assert.Equal("https://example.com", target);
    }

    [Theory]
    [InlineData(MouseEventKind.Down, MouseKeyModifiers.None)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Shift)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Alt)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Control | MouseKeyModifiers.Shift)]
    public void OtherMouseActionsDoNotActivateLink(
        MouseEventKind kind,
        MouseKeyModifiers modifiers)
    {
        var mouse = new MouseConsoleInputEvent(
            6, 2, MouseButton.Left, kind, modifiers);

        Assert.False(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out _));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(9, 2)]
    [InlineData(6, 1)]
    [InlineData(6, 3)]
    public void ClickOutsideHalfOpenBoundsDoesNotActivateLink(int x, int y)
    {
        var mouse = new MouseConsoleInputEvent(
            x, y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None);

        Assert.False(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out _));
    }
}
""")

write_new("tests/CSharpFar.Tests/SystemUriLauncherTests.cs", """using System.Diagnostics;
using CSharpFar.Shell;

namespace CSharpFar.Tests;

public sealed class SystemUriLauncherTests
{
    [Fact]
    public void OpenUsesSystemAssociationWithoutShellCommandConstruction()
    {
        ProcessStartInfo? captured = null;
        var launcher = new SystemUriLauncher(info =>
        {
            captured = info;
            return new Process();
        });

        launcher.Open(new Uri("https://example.com/docs?q=test#section"));

        Assert.NotNull(captured);
        Assert.Equal("https://example.com/docs?q=test#section", captured.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Empty(captured.ArgumentList);
    }

    [Fact]
    public void OpenRejectsRelativeUri()
    {
        var launcher = new SystemUriLauncher(_ => new Process());

        Assert.Throws<ArgumentException>(() => launcher.Open(new Uri("docs/readme.md", UriKind.Relative)));
    }
}
""")

normal_workflow = """name: Build

on:
  push:
    branches:
      - "**"
  workflow_dispatch:

permissions:
  contents: read

env:
  DOTNET_NOLOGO: true

jobs:
  build:
    name: Build and test (${{ matrix.os }})
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore
        run: dotnet restore CSharpFar.slnx

      - name: Build
        run: dotnet build CSharpFar.slnx --configuration Release --no-restore

      - name: Test
        run: dotnet test CSharpFar.slnx --configuration Release --no-build --verbosity minimal

      - name: Verify reusable packages
        shell: pwsh
        run: ./eng/verify-reusable-packages.ps1
"""
(ROOT / ".github/workflows/build.yml").write_text(normal_workflow, encoding="utf-8", newline="\n")
this_file = Path(__file__)
this_file.unlink()
try:
    this_file.parent.rmdir()
except OSError:
    pass
