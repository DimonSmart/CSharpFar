using CSharpFar.Core.Text;
using CSharpFar.Ui;

namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private void RequestEncodingChange(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        TextEncodingSelection selection,
        long anchorByteOffset,
        LargeFileViewMode baseViewMode,
        int contentHeight,
        int width)
    {
        _pendingNavigation.Clear();
        _navigationRequestVersion++;
        InvalidatePreparedRender();
        long stateVersion = _async.InvalidateState();

        EncodingChangeInput input = CaptureEncodingChangeInput(
            state,
            selection,
            anchorByteOffset,
            baseViewMode,
            contentHeight,
            width);

        _async.StartLatest(
            "encoding",
            stateVersion,
            cancellationToken => PrepareEncodingChangeAsync(
                sourcePath,
                reader,
                input,
                cancellationToken),
            result =>
            {
                ApplyEncodingChange(state, result);
                InvalidatePreparedRender();
            },
            ex => _dialogs.Message("Viewer", ex.Message));
    }

    private EncodingChangeResult? ApplyEncodingChangeWithDialog(
        string sourcePath,
        IFileByteReader reader,
        LargeFileViewerState state,
        TextEncodingSelection selection,
        long anchorByteOffset,
        LargeFileViewMode baseViewMode,
        int contentHeight,
        int width)
    {
        EncodingChangeInput input = CaptureEncodingChangeInput(
            state,
            selection,
            anchorByteOffset,
            baseViewMode,
            contentHeight,
            width);

        EncodingChangeResult? result = _dialogs.Operation<string, EncodingChangeResult, EncodingChangeResult?>(
            new OperationDialogDefinition<string, EncodingChangeResult, EncodingChangeResult?>
            {
                Title = "Encoding",
                PreferredWidth = 52,
                PreferredHeight = 8,
                MinWidth = 30,
                MinHeight = 8,
                Operation = cancellationToken => PrepareEncodingChangeAsync(
                    sourcePath,
                    reader,
                    input,
                    cancellationToken),
                Synchronize = () => new OperationDialogState<string>(
                    status: "Reading text with selected encoding... (Esc to cancel)"),
                HandleCancel = () =>
                    OperationDialogOutcome<EncodingChangeResult?>.Complete(null),
                HandleOperationCompleted = prepared =>
                    OperationDialogOutcome<EncodingChangeResult?>.Complete(prepared),
                Complete = prepared => prepared,
            });

        if (result is not null)
        {
            ApplyEncodingChange(state, result);
            InvalidatePreparedRender();
        }

        return result;
    }

    private static EncodingChangeInput CaptureEncodingChangeInput(
        LargeFileViewerState state,
        TextEncodingSelection selection,
        long anchorByteOffset,
        LargeFileViewMode baseViewMode,
        int contentHeight,
        int width) =>
        new(
            state.BlockCache,
            selection,
            anchorByteOffset,
            baseViewMode,
            state.LiveMode,
            state.WrapLines,
            state.WordWrap,
            state.PresentationMode,
            contentHeight,
            width);

    private async Task<EncodingChangeResult> PrepareEncodingChangeAsync(
        string sourcePath,
        IFileByteReader reader,
        EncodingChangeInput input,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        LineScanner scanner = await LineScanner
            .CreateAsync(input.Cache, reader, input.Selection, cancellationToken)
            .ConfigureAwait(false);

        LargeFileViewMode targetViewMode =
            input.BaseViewMode == LargeFileViewMode.Hex &&
            input.Selection.Kind == TextEncodingSelectionKind.Explicit
                ? LargeFileViewMode.Text
                : input.BaseViewMode;

        var candidate = new LargeFileViewerState(input.Cache, scanner)
        {
            ViewMode = targetViewMode,
            LiveMode = input.LiveMode,
            WrapLines = input.WrapLines,
            WordWrap = input.WordWrap,
            PresentationMode = input.PresentationMode,
            HorizontalOffset = 0,
            TopWrappedSegmentIndex = 0,
        };
        candidate.TopByteOffset = candidate.IsHexMode
            ? Math.Clamp(input.AnchorByteOffset, 0, reader.Length)
            : await scanner
                .FindLineStartAtOrBeforeAsync(input.AnchorByteOffset, cancellationToken)
                .ConfigureAwait(false);

        await NormalizeViewportAsync(
                sourcePath,
                reader,
                candidate,
                input.ContentHeight,
                input.Width,
                keepTail: true,
                cancellationToken)
            .ConfigureAwait(false);

        return new EncodingChangeResult(
            scanner,
            input.Selection,
            targetViewMode,
            candidate.TopByteOffset,
            candidate.TopWrappedSegmentIndex);
    }

    private static void ApplyEncodingChange(
        LargeFileViewerState state,
        EncodingChangeResult result)
    {
        state.ResetScanner(result.Scanner, result.Selection);
        state.ViewMode = result.ViewMode;
        state.TopByteOffset = result.TopByteOffset;
        state.TopWrappedSegmentIndex = result.TopWrappedSegmentIndex;
        state.HorizontalOffset = 0;
    }

    private sealed record EncodingChangeInput(
        BlockCache Cache,
        TextEncodingSelection Selection,
        long AnchorByteOffset,
        LargeFileViewMode BaseViewMode,
        ViewerLiveMode LiveMode,
        bool WrapLines,
        bool WordWrap,
        ViewerPresentationMode PresentationMode,
        int ContentHeight,
        int Width);

    private sealed record EncodingChangeResult(
        LineScanner Scanner,
        TextEncodingSelection Selection,
        LargeFileViewMode ViewMode,
        long TopByteOffset,
        int TopWrappedSegmentIndex);
}
