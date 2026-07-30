using System.Net.NetworkInformation;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.Module.ProcessesAndPorts;

internal sealed class ProcessesAndPortsDialog(ModuleUiServices ui, IProcessesAndPortsPlatformService platform)
{
    private readonly ModuleUiServices _ui = ui;
    private readonly IProcessesAndPortsPlatformService _platform = platform;

    public void Show(string? initialFilter)
    {
        var filter = new CommandLineState(); filter.SetText(initialFilter ?? string.Empty);
        var tcp = new CheckBoxRow(new CheckBoxLine("TCP listeners", true)) { Id = "tcp" };
        var udp = new CheckBoxRow(new CheckBoxLine("UDP endpoints", true)) { Id = "udp" };
        var other = new CheckBoxRow(new CheckBoxLine("Other TCP connections")) { Id = "other" };
        ProcessesAndPortsSnapshot snapshot = Capture();
        while (true)
        {
            var form = new ScrollableFormDialog();
            var input = new LabeledTextInputRow("Filter:", filter, labelWidth: 9) { Id = "filter" };
            var actions = new ButtonRow([
                new DialogButton("details", "Details", 'D'), new DialogButton("refresh", "Refresh", 'R'),
                new DialogButton("terminate", "Terminate", 'T'), new DialogButton("close", "Close", 'C', Role: DialogButtonRole.Cancel)])
            { Id = "actions" };
            void Prepare()
            {
                IReadOnlyList<ProcessNetworkEndpoint> items = FilterAndSort(snapshot.Endpoints, filter.Text, tcp.Value, udp.Value, other.Value);
                var rows = new List<IFormRow> { input, tcp, udp, other, new LabelRow("Process                 PID    Proto  Local address                         Port   State", FarDialogStyles.Fill) };
                rows.AddRange(items.Select(endpoint => new LabelRow(Format(endpoint), FarDialogStyles.Fill)));
                rows.Add(new LabelRow(items.Count == 0 ? "No matching endpoints." : Status(items, snapshot.Endpoints.Count), FarDialogStyles.Fill));
                form.SetRows(rows, [actions]);
            }
            Prepare(); form.SetInitialFocus("filter");
            string? action = new ModalFormHost(_ui.ModalDialogs).Run(
                form,
                new ModalFormOptions("Processes and Ports", 92, 24),
                layout => new ModalFormLayout(new(layout.ContentBounds.X, layout.ContentBounds.Y, layout.ContentBounds.Width, Math.Max(0, layout.ContentBounds.Height - 1)), new(layout.ContentBounds.X, Math.Max(layout.ContentBounds.Y, layout.ContentBounds.Bottom - 1), layout.ContentBounds.Width, 1)),
                (routed, result) => routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F5 }
                    ? ModalDialogLoopResult<string?>.Complete("refresh")
                    : routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete }
                        ? ModalDialogLoopResult<string?>.Complete("terminate")
                    : result.Kind switch
                    {
                        FormInputResultKind.Cancel => ModalDialogLoopResult<string?>.Complete(null),
                        FormInputResultKind.Submit => ModalDialogLoopResult<string?>.Complete(result.Command),
                        _ => ModalDialogLoopResult<string?>.ContinueNoChange,
                    },
                prepareRender: Prepare);
            if (action is null or "close") return;
            IReadOnlyList<ProcessNetworkEndpoint> shown = FilterAndSort(snapshot.Endpoints, filter.Text, tcp.Value, udp.Value, other.Value);
            ProcessNetworkEndpoint? selected = shown.FirstOrDefault();
            if (action == "refresh") { snapshot = Capture(); continue; }
            if (selected is null) continue;
            if (action == "details") { ShowDetails(snapshot, selected); continue; }
            if (action == "terminate")
            {
                if (selected.Process.ProcessId == Environment.ProcessId) { _ui.ShowMessage("Processes and Ports", "CSharpFar cannot terminate its own process."); continue; }
                if (!_ui.Confirm("Processes and Ports", "Terminate process?", $"{selected.Process.Name ?? "<unknown>"} ({selected.Process.ProcessId})")) continue;
                ProcessTerminationResult result = _platform.TerminateProcess(selected.Process.Identity);
                if (result.Status != ProcessTerminationStatus.Success) _ui.ShowMessage("Processes and Ports", result.Message ?? result.Status.ToString());
                else snapshot = Capture();
            }
        }
    }

    private ProcessesAndPortsSnapshot Capture()
    {
        try { return _platform.CaptureSnapshot(new(true, true, true)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { _ui.ShowMessage("Processes and Ports", ex.Message); return new(DateTimeOffset.Now, []); }
    }
    private void ShowDetails(ProcessesAndPortsSnapshot snapshot, ProcessNetworkEndpoint endpoint)
    {
        ProcessSnapshot process = endpoint.Process;
        var lines = new List<string> { $"Process:       {process.Name ?? "<unavailable>"}", $"PID:           {process.ProcessId}", $"Executable:    {process.ExecutablePath ?? "<access denied>"}", $"Started:       {process.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "<unavailable>"}", $"Snapshot time: {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss}", "", "Network endpoints:" };
        lines.AddRange(snapshot.Endpoints.Where(x => x.Process.ProcessId == process.ProcessId).Select(Format));
        _ui.ShowHelp("Processes and Ports details", lines);
    }
    internal static IReadOnlyList<ProcessNetworkEndpoint> FilterAndSort(IReadOnlyList<ProcessNetworkEndpoint> endpoints, string filter, bool includeTcp = true, bool includeUdp = true, bool includeOtherTcp = false) => endpoints
        .Where(x => x.Protocol == NetworkTransportProtocol.Udp ? includeUdp : x.TcpState == TcpState.Listen ? includeTcp : includeOtherTcp)
        .Where(x => SearchText(x).Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.LocalPort).ThenBy(x => x.Protocol).ThenBy(x => x.LocalAddress.ToString(), StringComparer.Ordinal).ThenBy(x => x.Process.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Process.ProcessId).ThenBy(x => x.TcpState).ToArray();
    private static string SearchText(ProcessNetworkEndpoint x) => string.Join(' ', x.Process.Name, x.Process.ProcessId, x.Process.ExecutablePath, x.Protocol, x.LocalAddress, x.LocalPort, x.RemoteAddress, x.RemotePort, x.TcpState);
    private static string Status(IReadOnlyList<ProcessNetworkEndpoint> items, int total) => items.Count == total ? $"{items.Count} endpoints, {items.Select(x => x.Process.ProcessId).Distinct().Count()} processes" : $"{items.Count} of {total} endpoints, {items.Select(x => x.Process.ProcessId).Distinct().Count()} processes";
    private static string Format(ProcessNetworkEndpoint x) => $"{(x.Process.Name ?? "<unknown>"),-22} {x.Process.ProcessId,6}  {x.Protocol,-5} {x.LocalAddress,-36} {x.LocalPort,5}  {(x.TcpState == TcpState.Listen ? "Listen" : x.TcpState?.ToString() ?? "-")}";
}
