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

    public void Show(string? initialFilter)
    {
        var filter = new CommandLineState(); filter.SetText(initialFilter ?? string.Empty);
        var tcp = new CheckBoxRow(new CheckBoxLine("TCP listeners", true)) { Id = "tcp" };
        var udp = new CheckBoxRow(new CheckBoxLine("UDP endpoints", true)) { Id = "udp" };
        var other = new CheckBoxRow(new CheckBoxLine("Other TCP connections")) { Id = "other" };
        var form = new ScrollableFormDialog();
        DialogButton[] actionButtons = [
            new DialogButton("details", "Details", 'D'), new DialogButton("refresh", "Refresh", 'R'),
            new DialogButton("terminate", "Terminate", 'T'), new DialogButton("close", "Close", 'C', Role: DialogButtonRole.Cancel)];
        var actions = new ButtonRow(actionButtons) { Id = "actions" };
        form.SetRows([new LabeledTextInputRow("Filter:", filter, labelWidth: 9) { Id = "filter" }, tcp, udp, other], [actions]);

        ProcessesAndPortsSnapshot? snapshot = TryCapture(null, out string? captureError);
        var targets = new UiTargetScope("processes-and-ports");
        var list = new RoutedScrollableList<ProcessesAndPortsRow>([], x => x.SearchText, targets.Child("endpoints"), targets.Child("endpoints.scrollbar"))
        {
            EmptyText = "No matching endpoints.",
            NormalStyle = FarDialogStyles.Fill,
            SelectedStyle = FarDialogStyles.FocusedInput,
        };
        IReadOnlyList<ProcessesAndPortsRow> lastRows = [];

        _ui.ModalDialogs.RunInteractive<Frame, Input, object?>(
            (context, focus) =>
            {
                IReadOnlyList<ProcessesAndPortsRow> rows = Project(snapshot?.Endpoints ?? [], filter.Text, tcp.Value, udp.Value, other.Value);
                if (!SameRows(rows, lastRows))
                {
                    list.ReplaceItems(rows, row => row.Key, Math.Max(0, context.Size.Height - 10));
                    lastRows = rows;
                }
                bool canTerminate = CanTerminate(list.SelectedItemOrDefault?.Endpoint);
                actions.SetButtons(actionButtons.Select(button => button with
                {
                    IsEnabled = button.Id == "details" ? list.HasItems : button.Id == "terminate" ? canTerminate : button.IsEnabled,
                }).ToArray());
                return Draw(context, focus, form, list, snapshot, captureError, rows, tcp.Value, udp.Value, other.Value);
            },
            frame => new UiInteractionFrameBuilder()
                .AddFragment(list.BuildInteractionFragment(frame.List, 1, frame.ListBounds.Width > 0 && frame.ListBounds.Height > 0))
                .AddFragment(form.BuildInteractionFragment(frame.Form))
                .SetDefaultFocusTarget(frame.Form.DefaultTarget ?? list.ListTarget)
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
                    ShowSelectedDetails(snapshot, list.SelectedItemOrDefault?.Endpoint);
                    return ModalDialogLoopResult<object?>.ContinueNoChange;
                }
                string? action = result.Form.Command;
                if (action is null) return ModalDialogLoopResult<object?>.ContinueNoChange;
                if (action == "close") return ModalDialogLoopResult<object?>.Complete(null);
                if (action == "refresh") { snapshot = TryCapture(snapshot, out captureError); return ModalDialogLoopResult<object?>.ContinueChanged; }
                if (action == "details") { ShowSelectedDetails(snapshot, list.SelectedItemOrDefault?.Endpoint); return ModalDialogLoopResult<object?>.ContinueNoChange; }
                if (action == "terminate") Terminate(snapshot, list.SelectedItemOrDefault?.Endpoint, ref captureError, ref snapshot);
                return ModalDialogLoopResult<object?>.ContinueChanged;
            },
            applyCommittedFrame: frame => list.ApplyCommittedFrame(frame.List));
    }

    private static (Input Semantic, UiInputResult Ui) Route(ConsoleInputEvent input, Frame frame, UiInputRouteContext route, ScrollableFormDialog form, RoutedScrollableList<ProcessesAndPortsRow> list)
    {
        list.ApplyCommittedFrame(frame.List);
        if (list.IsTargetRoute(route))
        {
            RoutedScrollableListInputResult listResult = list.RouteInput(input, frame.List, route, confirmOnDoubleClick: true);
            if (listResult.ListResult.IsHandled)
                return (new(input, FormInputResult.NotHandled, listResult.ListResult), listResult.UiResult);
            if (input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Delete } && list.SelectedItemOrDefault is not null)
                return (new(input, new FormInputResult(FormInputResultKind.Submit, "terminate"), ScrollableListInputResult.Handled), UiInputResult.HandledResult);
            if (UiFocusRouting.TryHandleTraversal(input, out UiInputResult traversal))
                return (new(input, FormInputResult.NotHandled, listResult.ListResult), traversal);
        }
        FormRouteResult formResult = form.RouteInput(input, frame.Form, route, allowUnfocusedButtonHotkeys: true);
        return (new(input, formResult.FormResult, ScrollableListInputResult.NotHandled), formResult.UiResult);
    }

    private Frame Draw(UiRenderContext context, IUiFocusState focus, ScrollableFormDialog form, RoutedScrollableList<ProcessesAndPortsRow> list, ProcessesAndPortsSnapshot? snapshot, string? captureError, IReadOnlyList<ProcessesAndPortsRow> rows, bool tcp, bool udp, bool other)
    {
        ModalDialogRenderer.Layout modal = _renderer.CalculateLayout(context.Size, DialogWidth, 24);
        Rect content = modal.ContentBounds;
        Rect formBody = new(content.X, content.Y, content.Width, Math.Min(4, content.Height));
        Rect footer = new(content.X, Math.Max(content.Y, content.Bottom - 1), content.Width, content.Height > 4 ? 1 : 0);
        Rect header = new(content.X, formBody.Bottom, content.Width, formBody.Bottom < footer.Y ? 1 : 0);
        Rect status = new(content.X, Math.Max(header.Bottom, footer.Y - 1), content.Width, footer.Y - header.Bottom > 0 ? 1 : 0);
        Rect listBounds = new(content.X, header.Bottom, content.Width, Math.Max(0, status.Y - header.Bottom));
        Rect? scrollbar = list.Count > listBounds.Height && listBounds.Height > 0 ? new Rect(content.Right - 1, listBounds.Y, 1, listBounds.Height) : null;
        RoutedScrollableListFrame listFrame = list.CalculateFrame(listBounds.Height, listBounds, scrollbar);
        ScrollableFormFrame formFrame = null!;
        _renderer.Render(context.Canvas, modal, "Processes and Ports", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, _) =>
        {
            formFrame = form.Render(new FormRenderContext(context, formBody, FarDialogStyles.Border, footer), focus, [new UiFocusEntry(list.ListTarget, 1, list.HasItems)], list.HasItems ? list.ListTarget : null);
            if (header.Height > 0) context.Canvas.Write(header.X, header.Y, Fit("Process                 PID  Proto  Port  Local address                 State  Remote", header.Width), FarDialogStyles.Fill);
            list.Render(context.Canvas, listFrame, FarDialogStyles.Fill, FarDialogStyles.FocusedInput, FarDialogStyles.Fill);
            list.RenderScrollbar(context.Canvas, listFrame, FarDialogStyles.Border);
            if (status.Height > 0) context.Canvas.Write(status.X, status.Y, Fit(captureError ?? Status(rows, AllowedCount(snapshot?.Endpoints ?? [], tcp, udp, other)), status.Width), FarDialogStyles.Fill);
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
    private static string Fit(string value, int width) => value.Length <= width ? value.PadRight(width) : value[..Math.Max(0, width)];
    private readonly record struct Frame(ModalDialogRenderer.Layout Modal, Rect ListBounds, RoutedScrollableListFrame List, ScrollableFormFrame Form);
    private readonly record struct Input(ConsoleInputEvent InputEvent, FormInputResult Form, ScrollableListInputResult List);
}
