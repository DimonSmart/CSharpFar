using System.Globalization;
using System.Text;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{


























    private static WrappedLineLayout CreateWrappedLineLayout(
        PresentedLine presented,
        bool wordWrap,
        int width)
    {
        WrappedTextSegment[] segments = presented.PreserveLineLayout
            ? [new WrappedTextSegment(0, presented.Text)]
            : SplitWrappedLine(presented.Text, Math.Max(1, width), wordWrap).ToArray();
        return new WrappedLineLayout(presented, segments);
    }

    private bool TryGetWrappedLineLayout(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width,
        out WrappedLineLayout layout)
    {
        EnsureLayoutCacheContext(state);
        return _wrappedLayoutCache.TryGet(
            CreateWrappedLayoutCacheKey(sourcePath, state, line, width),
            out layout);
    }

    private void StoreWrappedLineLayout(
        WrappedLayoutCacheKey key,
        WrappedLineLayout layout) =>
        _wrappedLayoutCache.Set(key, layout);

    private WrappedLayoutCacheKey CreateWrappedLayoutCacheKey(
        string sourcePath,
        LargeFileViewerState state,
        ScannedLine line,
        int width)
    {
        ViewerSearchMatch? match = state.SearchMatch is { IsHex: false } candidate &&
                                   candidate.LineStartOffset == line.StartOffset
            ? candidate
            : null;
        return new WrappedLayoutCacheKey(
            state.LineScanner,
            sourcePath,
            line.StartOffset,
            line.NextOffset,
            Math.Max(1, width),
            state.WordWrap,
            state.PresentationMode,
            match?.CharacterIndex ?? -1,
            match?.CharacterLength ?? 0);
    }

    private MaximumViewportCacheKey CreateMaximumViewportCacheKey(
        string sourcePath,
        LargeFileViewerState state,
        long sourceLength,
        int contentHeight,
        int width)
    {
        ViewerSearchMatch? match = state.SearchMatch is { IsHex: false } candidate
            ? candidate
            : null;
        return new MaximumViewportCacheKey(
            state.LineScanner,
            sourcePath,
            sourceLength,
            Math.Max(1, contentHeight),
            Math.Max(1, width),
            state.ViewMode,
            state.WrapLines,
            state.WordWrap,
            state.PresentationMode,
            match?.LineStartOffset ?? -1,
            match?.CharacterIndex ?? -1,
            match?.CharacterLength ?? 0);
    }

    private void EnsureLayoutCacheContext(LargeFileViewerState state)
    {
        if (ReferenceEquals(_layoutCacheState, state) &&
            ReferenceEquals(_layoutCacheScanner, state.LineScanner))
        {
            return;
        }

        _layoutCacheState = state;
        _layoutCacheScanner = state.LineScanner;
        ClearWrappedLayoutCache();
    }

    private void ClearWrappedLayoutCache() => _wrappedLayoutCache.Clear();

    private static bool RequiresStreamingWrappedLayout(ScannedLine line) =>
        line.NextOffset - line.StartOffset > MaxWrappedLineCaptureBytes;



    private void WriteRawWrappedTextLine(
        IUiCanvas canvas,
        string text,
        int y,
        int width)
    {
        var layout = new ViewerTextLayout(text);
        canvas.WriteForced(
            0,
            y,
            layout.Slice(0, Math.Max(1, width)),
            CSharpFarPaletteStyles.CommandLine(_palette));
    }





    private static void SetLiveMode(
        LocalViewerSession? session,
        LargeFileViewerState state,
        ViewerLiveMode newMode)
    {
        ViewerLiveMode oldMode = state.LiveMode;
        if (oldMode == newMode)
            return;

        if (newMode != ViewerLiveMode.Off && session is null)
            return;

        if (oldMode == ViewerLiveMode.Off && newMode != ViewerLiveMode.Off)
            session!.StartMonitoring();
        else if (oldMode != ViewerLiveMode.Off && newMode == ViewerLiveMode.Off)
            session?.StopMonitoring();

        state.LiveMode = newMode;
    }





    private static int CompareViewportPositions(
        ViewerViewportPosition left,
        ViewerViewportPosition right)
    {
        int byteComparison = left.ByteOffset.CompareTo(right.ByteOffset);
        return byteComparison != 0
            ? byteComparison
            : left.WrappedSegmentIndex.CompareTo(right.WrappedSegmentIndex);
    }

    private static void ApplyViewportPosition(
        LargeFileViewerState state,
        ViewerViewportPosition position)
    {
        state.TopByteOffset = position.ByteOffset;
        state.TopWrappedSegmentIndex = position.WrappedSegmentIndex;
    }

    private void MoveHorizontal(LargeFileViewerState state, int delta)
    {
        if (state.WrapLines)
            return;

        state.HorizontalOffset = Math.Max(0, state.HorizontalOffset + delta);
    }

    private static void MoveHorizontalToCurrentLineEnd(
        LargeFileViewerState state,
        LargeFileRenderView view,
        int width)
    {
        if (state.WrapLines || view.Lines.Count == 0)
            return;

        int lineLength = new ViewerTextLayout(view.Lines[0].Text).CellWidth;
        state.HorizontalOffset = Math.Max(0, lineLength - Math.Max(1, width));
    }

    private void JumpToPosition(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        int contentHeight,
        int width,
        LocalViewerSession? localSession)
    {
        string? input = _dialogs.Input(new SingleLineInputDialogOptions
        {
            Title = "Viewer",
            Prompt = state.IsHexMode ? "Percent or byte offset:" : "Line number or percent:",
            Validate = text => ValidateJump(text, state.IsHexMode),
        });
        if (input is null)
            return;

        ViewerNavigationRequest request;
        if (input.EndsWith('%'))
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpPercent,
                int.Parse(input[..^1], CultureInfo.InvariantCulture));
        }
        else if (state.IsHexMode)
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpByte,
                long.Parse(input, CultureInfo.InvariantCulture));
        }
        else
        {
            request = new ViewerNavigationRequest(
                ViewerNavigationKind.JumpLine,
                long.Parse(input, CultureInfo.InvariantCulture));
        }

        QueueNavigation(
            sourcePath,
            reader,
            state,
            contentHeight,
            width,
            localSession,
            request);
    }


}
