namespace CSharpFar.App.Viewer;

internal sealed partial class LargeFileViewer
{
    private void RequestLocalMonitorPoll(
        LocalViewerSession session,
        string filePath,
        int contentHeight,
        int width)
    {
        if (session.State.LiveMode == ViewerLiveMode.Off ||
            session.PendingRefresh ||
            _async.IsChannelPending("monitor") ||
            _async.IsChannelPending("refresh"))
        {
            return;
        }

        long stateVersion = _async.StateVersion;
        _async.StartLatest<LocalFileSnapshot?>(
            "monitor",
            stateVersion,
            async cancellationToken =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return LocalFileMonitor.TryCaptureSnapshot(
                    filePath,
                    out LocalFileSnapshot snapshot)
                        ? snapshot
                        : null;
            },
            snapshot =>
            {
                if (snapshot is null)
                    return;

                session.ObservePolledSnapshot(snapshot.Value);
                if (session.PendingRefresh)
                    RequestLocalRefresh(session, filePath, contentHeight, width);
            });
    }
}
