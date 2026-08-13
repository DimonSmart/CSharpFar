using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.Module.ProcessesAndPorts;

internal readonly record struct EndpointKey(NetworkTransportProtocol Protocol, IPAddress LocalAddress, int LocalPort, IPAddress? RemoteAddress, int? RemotePort, int ProcessId);
internal sealed record ProcessesAndPortsRow(EndpointKey Key, ProcessNetworkEndpoint Endpoint, string SearchText);

internal sealed class ProcessesAndPortsDialog(ModuleUiServices ui, IProcessesAndPortsPlatformService platform)
{
    private const int DialogWidth = 92;
    private readonly ModuleUiServices _ui = ui;
    private readonly IProcessesAndPortsPlatformService _platform = platform;
    private readonly ModalDialogRenderer _renderer = new();
    private static readonly TableListDefinition<ProcessesAndPortsRow> TableDefinition = new()
    {
        Columns = [
            TableColumn<ProcessesAndPortsRow>.Text("Process", row => DisplayName(row.Endpoint.Process), TableWidth.Flexible(22, 8)),
            TableColumn<ProcessesAndPortsRow>.Text("PID", row => row.Endpoint.Process.ProcessId.ToString(), 6, TableColumnAlignment.Right),
            TableColumn<ProcessesAndPortsRow>.Text("Proto", row => row.Endpoint.Protocol == NetworkTransportProtocol.Tcp ? "TCP" : "UDP", 5),
            TableColumn<ProcessesAndPortsRow>.Text("Port", row => row.Endpoint.LocalPort.ToString(), 5, TableColumnAlignment.Right),
            TableColumn<ProcessesAndPortsRow>.Text("Local address", row => Address(row.Endpoint.LocalAddress), TableWidth.Flexible(28, 8)),
            TableColumn<ProcessesAndPortsRow>.Text("State", row => row.Endpoint.Protocol == NetworkTransportProtocol.Udp ? "-" : row.Endpoint.TcpState == TcpState.Listen ? "Listen" : row.Endpoint.TcpState?.ToString() ?? "", TableWidth.Optional(11, 0, 1)),
            TableColumn<ProcessesAndPortsRow>.Text("Remote", row => row.Endpoint.RemoteAddress is null ? string.Empty : Address(row.Endpoint.RemoteAddress, row.Endpoint.RemotePort), TableWidth.Optional(20, 0, 0)),
        ],
    };

    public void Show(string? initialFilter)
    {
        TextField filter = _ui.Fields.Text(initialFilter ?? string.Empty);
        var tcp = FormControls.CheckBox("TCP listeners", true);
        var udp = FormControls.CheckBox("UDP endpoints", true);
        var other = FormControls.CheckBox("Other TCP connections");
        var form = new ScrollableFormDialog();
        DialogButton[] actionButtons = [
            DialogButton.Action("details", "Details", 'D'), DialogButton.Action("refresh", "Refresh", 'R'),
            DialogButton.Action("terminate", "Terminate", 'T'), DialogButton.Cancel("Close", 'C', "close")];
        var actions = FormControls.Buttons(actionButtons);
        form.SetRows([FormControls.Text("Filter:", filter), tcp, udp, other], [actions]);

        ProcessesAndPortsSnapshot? snapshot = TryCapture(null, out string? captureError);
        var list = new TableList<ProcessesAndPortsRow>([], TableDefinition, appearance: ListAppearance.Dialog);
        IReadOnlyList<ProcessesAndPortsRow> lastRows = [];
        ProcessNetworkEndpoint? SelectedEndpoint() => list.TryGetSelectedItem(out ProcessesAndPortsRow selected) ? selected.Endpoint : null;

        _ui.ModalDialogs.RunInteractive<Frame, Input, object?>(
            (context, focus) =>
            {
                IReadOnlyList<ProcessesAndPortsRow> rows = Project(snapshot?.Endpoints ?? [], filter.Text, tcp.Value, udp.Value, other.Value);
                if (!SameRows(rows, lastRows))
                {
                    list.ReplaceItems(rows, row => row.Key);
                    lastRows = rows;
                }
                bool canTerminate = CanTerminate(SelectedEndpoint());
                actions.SetButtons(actionButtons.Select(button => button with
                {
                    IsEnabled = button.Id == "details" ? list.HasItems : button.Id == "terminate" ? canTerminate : button.IsEnabled,
                }).ToArray());
                return Draw(context, focus, form, list, snapshot, captureError, rows, tcp.Value, udp.Value, other.Value);
            },
            frame => new UiInteractionFrameBuilder()
                .AddFragment(list.BuildInteractionFragment(frame.List, 1))
                .AddFragment(form.BuildInteractionFragment(frame.Form))
                .SetDefaultFocusTarget(frame.Form.DefaultTarget)
                .Build(),
            (input, frame, route) => Route(input, frame, route, form, list),
            (routed, result) =>
            {
                if (routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape or ConsoleKey.F10 })
                    return ModalDialogLoopResult<object?>.Complete(null);
                if (result.Form.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<object?>.Complete(null);
                if (routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F5 })
                {
                    snapshot = TryCapture(snapshot, out captureError);
                    return ModalDialogLoopResult<object?>.ContinueChanged;
                }
                if (result.List.Kind == ScrollableListInputResultKind.Confirmed)
                {
                    ShowSelectedDetails(snapshot, SelectedEndpoint());
                    return ModalDialogLoopResult<object?>.ContinueNoChange;
                }
                string? action = result.Form.Command;
                if (action is null) return ModalDialogLoopResult<object?>.ContinueNoChange;
                if (action == "close") return ModalDialogLoopResult<object?>.Complete(null);
                if (action == "refresh") { snapshot = TryCapture(snapshot, out captureError); return ModalDialogLoopResult<object?>.ContinueChanged; }
                if (action == "details") { ShowSelectedDetails(snapshot, SelectedEndpoint()); return ModalDialogLoopResult<object?>.ContinueNoChange; }
                if (action == "terminate") Terminate(snapshot, SelectedEndpoint(), ref captureError, ref snapshot);
                return ModalDialogLoopResult<object?>.ContinueChanged;
            });
    }

    private static (Input Semantic, UiInputResult Ui) Route(ConsoleInputEvent input, Frame frame, UiInputRouteContext route, ScrollableFormDialog form, TableList<ProcessesAndPortsRow> list)
    {
        if (list.IsTargetRoute(route))
        {
            var listResult = list.RouteInput(input, frame.List, route);
            if (listResult.Semantic.IsHandled)
                return (new(input, FormInputResult.NotHandled, listResult.Semantic), listResult.UiResult);
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && list.HasItems)
                return (new(input, new FormInputResult(FormInputResultKind.Submit, "terminate"), ScrollableListInputResult.Handled), UiInputResult.HandledResult);
            if (UiFocusRouting.TryHandleTraversal(input, out UiInputResult traversal))
                return (new(input, FormInputResult.NotHandled, listResult.Semantic), traversal);
        }
        FormRouteResult formResult = form.RouteInput(input, frame.Form, route, allowUnfocusedButtonHotkeys: true);
        return (new(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
    }

    private Frame Draw(UiRenderContext context, IUiFocusState focus, ScrollableFormDialog form, TableList<ProcessesAndPortsRow> list, ProcessesAndPortsSnapshot? snapshot, string? captureError, IReadOnlyList<ProcessesAndPortsRow> rows, bool tcp, bool udp, bool other)
    {
        ModalDialogRenderer.Layout modal = _renderer.CalculateLayout(context.Size, DialogWidth, 24);
        Rect content = modal.ContentBounds;
        Rect formBody = new(content.X, content.Y, content.Width, Math.Min(4, content.Height));
        Rect footer = new(content.X, Math.Max(content.Y, content.Bottom - 1), content.Width, content.Height > 4 ? 1 : 0);
        Rect header = new(content.X, formBody.Bottom, content.Width, formBody.Bottom < footer.Y ? 1 : 0);
        Rect status = new(content.X, Math.Max(header.Bottom, footer.Y - 1), content.Width, footer.Y - header.Bottom > 0 ? 1 : 0);
        Rect listBounds = new(content.X, header.Bottom, content.Width, Math.Max(0, status.Y - header.Bottom));
        TableListFrame listFrame = list.CalculateFrame(listBounds);
        ScrollableFormFrame formFrame = null!;
        _renderer.Render(context.Canvas, modal, "Processes and Ports", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, _) =>
        {
            formFrame = form.Render(new FormRenderContext(context, formBody, FarDialogStyles.Border, footer), focus, [], null);
            list.Render(context.Canvas, listFrame);
            if (status.Height > 0) context.Canvas.Write(status.X, status.Y, ConsoleTextMetrics.FitToCells(captureError ?? Status(rows, AllowedCount(snapshot?.Endpoints ?? [], tcp, udp, other)), status.Width), FarDialogStyles.Fill);
        });
        return new(modal, listBounds, listFrame, formFrame);
    }

    private ProcessesAndPortsSnapshot? TryCapture(ProcessesAndPortsSnapshot? previous, out string? error)
    {
        try { error = null; return _platform.CaptureSnapshot(new(true, true, true)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or InvalidDataException)
        {
            error = $"Capture failed: {ex.Message}";
            _ui.ShowMessage("Processes and Ports", error);
            return previous;
        }
    }

    private bool CanTerminate(ProcessNetworkEndpoint? endpoint) => endpoint?.Process.Identity is not null && endpoint.Process.ProcessId > 0 && endpoint.Process.ProcessId != Environment.ProcessId && _platform.Support.CanTerminate;
    private void ShowSelectedDetails(ProcessesAndPortsSnapshot? snapshot, ProcessNetworkEndpoint? endpoint)
    {
        if (snapshot is null || endpoint is null) return;
        ProcessSnapshot process = endpoint.Process;
        var lines = new List<string> { $"Process:       {DisplayName(process)}", $"PID:           {process.ProcessId}", $"Executable:    {process.ExecutablePath ?? MetadataText(process.MetadataStatus)}", $"Started:       {process.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? MetadataText(process.MetadataStatus)}", $"Metadata:      {process.MetadataStatus}", $"Snapshot time: {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss}", "", "Network endpoints:" };
        lines.AddRange(snapshot.Endpoints.Where(x => x.Process.ProcessId == process.ProcessId).Select(Format));
        _ui.ShowHelp("Processes and Ports details", lines);
    }

    private void Terminate(ProcessesAndPortsSnapshot? snapshot, ProcessNetworkEndpoint? endpoint, ref string? error, ref ProcessesAndPortsSnapshot? current)
    {
        if (endpoint?.Process.Identity is not { } identity || !CanTerminate(endpoint)) return;
        string ports = string.Join(", ", snapshot?.Endpoints.Where(x => x.Process.ProcessId == endpoint.Process.ProcessId).Select(x => x.LocalPort).Distinct().Order().Select(x => x.ToString()) ?? []);
        string prompt = $"Process: {DisplayName(endpoint.Process)}\nPID: {endpoint.Process.ProcessId}\nExecutable: {endpoint.Process.ExecutablePath ?? "<unavailable>"}\nStarted: {endpoint.Process.StartedAt:yyyy-MM-dd HH:mm:ss}\nPorts: {ports}\n\nUnsaved data may be lost.";
        if (!_ui.Confirm("Processes and Ports", "Terminate process?", prompt)) return;
        ProcessTerminationResult result = _platform.TerminateProcess(identity);
        if (result.Status is ProcessTerminationStatus.Success or ProcessTerminationStatus.NotFound or ProcessTerminationStatus.AlreadyExited or ProcessTerminationStatus.StaleIdentity)
            current = TryCapture(current, out error);
        if (result.Status != ProcessTerminationStatus.Success)
            _ui.ShowMessage("Processes and Ports", result.Message ?? result.Status.ToString());
    }

    internal static IReadOnlyList<ProcessesAndPortsRow> Project(IReadOnlyList<ProcessNetworkEndpoint> endpoints, string filter, bool includeTcp = true, bool includeUdp = true, bool includeOtherTcp = false) => endpoints
        .Where(x => IsAllowed(x, includeTcp, includeUdp, includeOtherTcp))
        .Select(x => new ProcessesAndPortsRow(new(x.Protocol, x.LocalAddress, x.LocalPort, x.RemoteAddress, x.RemotePort, x.Process.ProcessId), x, SearchText(x)))
        .Where(x => x.SearchText.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.Endpoint.LocalPort).ThenBy(x => x.Endpoint.Protocol).ThenBy(x => x.Endpoint.LocalAddress.ToString(), StringComparer.Ordinal).ThenBy(x => x.Endpoint.Process.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Endpoint.Process.ProcessId).ThenBy(x => x.Endpoint.TcpState).ToArray();
    private static bool IsAllowed(ProcessNetworkEndpoint x, bool tcp, bool udp, bool other) => x.Protocol == NetworkTransportProtocol.Udp ? udp : x.TcpState == TcpState.Listen ? tcp : other;
    private static int AllowedCount(IReadOnlyList<ProcessNetworkEndpoint> endpoints, bool tcp, bool udp, bool other) => endpoints.Count(x => IsAllowed(x, tcp, udp, other));
    private static string SearchText(ProcessNetworkEndpoint x) => string.Join(' ', x.Process.Name, x.Process.ProcessId, x.Process.ExecutablePath, x.Protocol, x.LocalAddress, x.LocalPort, x.RemoteAddress, x.RemotePort, x.TcpState);
    private static string Status(IReadOnlyList<ProcessesAndPortsRow> items, int total) => items.Count == total ? $"{items.Count} endpoints, {items.Select(x => x.Endpoint.Process.ProcessId).Distinct().Count()} {(items.Select(x => x.Endpoint.Process.ProcessId).Distinct().Count() == 1 ? "process" : "processes")}" : $"{items.Count} of {total} endpoints, {items.Select(x => x.Endpoint.Process.ProcessId).Distinct().Count()} processes";
    private static string Format(ProcessNetworkEndpoint x) => $"{DisplayName(x.Process),-22} {x.Process.ProcessId,6}  {x.Protocol,-5} {x.LocalPort,5}  {Address(x.LocalAddress),-28} {(x.TcpState == TcpState.Listen ? "Listen" : x.TcpState?.ToString() ?? "-"),-11} {(x.RemoteAddress is null ? "" : Address(x.RemoteAddress, x.RemotePort))}";
    private static string Address(IPAddress address, int? port = null) => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]{(port is null ? "" : $":{port}")}" : $"{address}{(port is null ? "" : $":{port}")}";
    private static string DisplayName(ProcessSnapshot process) => process.Name ?? MetadataText(process.MetadataStatus);
    private static string MetadataText(ProcessMetadataStatus status) => status switch { ProcessMetadataStatus.AccessDenied => "<access denied>", ProcessMetadataStatus.Exited => "<process exited>", _ => "<unavailable>" };
    private static bool SameRows(IReadOnlyList<ProcessesAndPortsRow> left, IReadOnlyList<ProcessesAndPortsRow> right) => left.Count == right.Count && left.Zip(right).All(x => x.First.Key.Equals(x.Second.Key));
    private readonly record struct Frame(ModalDialogRenderer.Layout Modal, Rect ListBounds, TableListFrame List, ScrollableFormFrame Form);
    private readonly record struct Input(ConsoleInputEvent InputEvent, FormInputResult Form, ScrollableListInputResult List);
}
