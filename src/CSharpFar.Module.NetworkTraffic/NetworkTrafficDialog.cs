using CSharpFar.Module.Abstractions;
using CSharpFar.NetworkTraffic;
using CSharpFar.Ui;

namespace CSharpFar.Module.NetworkTraffic;

internal sealed class NetworkTrafficDialog(ModuleUiServices ui)
{
    public void Show(INetworkTrafficMonitor monitor, CancellationToken token)
    {
        var filter = ui.Fields.Text(new TextFieldOptions(""));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = Task.Run(() => monitor.RunAsync(stop.Token), CancellationToken.None);
        try
        {
            ui.Dialogs.Operation(new OperationDialogDefinition<TrafficApplication, bool, bool>
            {
                Title = "Network Traffic",
                ResizeMode = DialogResizeMode.Both,
                PreferredWidth = 100,
                PreferredHeight = 26,
                RefreshInterval = TimeSpan.FromMilliseconds(500),
                TableDefinition = new()
                {
                    Columns = [
                    TableColumn<TrafficApplication>.Text("Application",x=>x.Name,TableWidth.Flexible(28,12)),
                TableColumn<TrafficApplication>.Text("Window",x=>string.Join(" | ",x.WindowTitles),TableWidth.Optional(36,0,0)),
                TableColumn<TrafficApplication>.Text("Receive/s",x=>Bytes(x.ReceiveBytesPerSecond),12,TableColumnAlignment.Right),
                TableColumn<TrafficApplication>.Text("Send/s",x=>Bytes(x.SendBytesPerSecond),12,TableColumnAlignment.Right),
                TableColumn<TrafficApplication>.Text("Downloaded",x=>Bytes(x.ReceivedBytes),12,TableColumnAlignment.Right),
                TableColumn<TrafficApplication>.Text("Uploaded",x=>Bytes(x.SentBytes),12,TableColumnAlignment.Right),
            ]
                },
                ItemIdentity = x => x.Key,
                KeyboardCommands = new Dictionary<ConsoleKey, string> { [ConsoleKey.F10] = "close", [ConsoleKey.F1] = "help" },
                Operation = async _ => { await running.ConfigureAwait(false); return true; },
                OnCancellationRequested = stop.Cancel,
                Synchronize = () =>
                {
                    var s = monitor.Snapshot;
                    return new OperationDialogState<TrafficApplication>(
                        rows: [FormControls.Text("Filter:",filter),
                        FormControls.Label($"Unattributed: {Bytes(s.UnknownBytesPerSecond)}/s ({Bytes(s.UnknownBytes)} since opening)"),
                        FormControls.Label(s.EstimatedBytes > 0 ? "Some application matches are approximate." : "Downloaded / Uploaded: totals since monitoring started.")],
                        items: Filter(s.Applications, filter.Text),
                        buttons: [DialogButton.Action("help", "Help", 'H'), DialogButton.Cancel("Close", 'C', "close")],
                        status: s.Status + (RankingUncertain(s) ? " / Unattributed traffic could change the leader" : ""));
                },
                HandleCommand = action =>
                {
                    if (action.ActionId == "close") return OperationDialogOutcome<bool>.Complete(true);
                    if (action.ActionId == "help") ui.ShowHelp("Network Traffic", [
                        "Applications are sorted by Receive + Send over the last 10 seconds.",
                    "Collection starts when this screen opens. ETW delivery adds about 5 seconds of lag.",
                    "Esc / F10: close and stop monitoring.",
                    "One row per executable path, even when multiple processes or windows belong to it.",
                    "Application names come from executable descriptions; window captions use standard process APIs.",
                    "Window captions may be absent, especially for background or browser network processes.",
                    "Downloaded / Uploaded: bytes received and sent since monitoring started. Loopback is included.",
                    "Approximate matching identifies the app from connection snapshots; bytes themselves are measured.",
                    "Unattributed: traffic whose application could not be identified safely.",
                    "Capture loss makes subsequent attribution incomplete; reopen to start a new session.",
                    "Windows administrator access required. VPN/virtualized traffic may be incomplete."]);
                    return OperationDialogOutcome<bool>.ContinueChanged;
                },
                HandleCancel = () => OperationDialogOutcome<bool>.Complete(true),
                HandleOperationCompleted = _ => OperationDialogOutcome<bool>.ContinueChanged,
                Complete = _ => true,
            }, token);
        }
        finally { stop.Cancel(); running.GetAwaiter().GetResult(); }
    }
    private static bool RankingUncertain(TrafficSnapshot s)
    {
        if (s.UnknownBytesPerSecond <= 0) return false;
        double first = s.Applications.FirstOrDefault() is { } a ? a.ReceiveBytesPerSecond + a.SendBytesPerSecond : 0;
        double second = s.Applications.Skip(1).FirstOrDefault() is { } b ? b.ReceiveBytesPerSecond + b.SendBytesPerSecond : 0;
        return s.UnknownBytesPerSecond >= first - second;
    }
    internal static IReadOnlyList<TrafficApplication> Filter(IReadOnlyList<TrafficApplication> applications, string text) => applications
        .Where(x => (x.Name + " " + x.ExecutablePath + " " + string.Join(" ", x.WindowTitles) + " " + string.Join(" ", x.Processes))
            .Contains(text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static string Bytes(double value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"]; int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.0} {units[i]}";
    }
}
