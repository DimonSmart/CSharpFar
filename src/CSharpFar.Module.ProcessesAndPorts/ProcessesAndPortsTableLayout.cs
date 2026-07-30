using System.Net.NetworkInformation;
using CSharpFar.Platform.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.Module.ProcessesAndPorts;

internal enum TableTextAlignment
{
    Left,
    Right,
}

internal sealed record ProcessesAndPortsColumn(
    string Header,
    int PreferredWidth,
    int MinimumWidth,
    TableTextAlignment Alignment,
    Func<ProcessesAndPortsRow, string> Value);

internal readonly record struct CalculatedColumn(ProcessesAndPortsColumn Definition, int Offset, int Width);

internal sealed class ProcessesAndPortsTableLayout
{
    private const int SeparatorWidth = 2;
    private static readonly ProcessesAndPortsColumn[] Definitions =
    [
        new("Process", 22, 8, TableTextAlignment.Left, row => DisplayName(row.Endpoint.Process)),
        new("PID", 6, 6, TableTextAlignment.Right, row => row.Endpoint.Process.ProcessId.ToString()),
        new("Proto", 5, 5, TableTextAlignment.Left, row => row.Endpoint.Protocol == NetworkTransportProtocol.Tcp ? "TCP" : "UDP"),
        new("Port", 5, 5, TableTextAlignment.Right, row => row.Endpoint.LocalPort.ToString()),
        new("Local address", 28, 8, TableTextAlignment.Left, row => Address(row.Endpoint.LocalAddress)),
        new("State", 11, 0, TableTextAlignment.Left, row => State(row.Endpoint)),
        new("Remote", 20, 0, TableTextAlignment.Left, row => row.Endpoint.RemoteAddress is null ? string.Empty : Address(row.Endpoint.RemoteAddress, row.Endpoint.RemotePort)),
    ];

    private readonly IReadOnlyList<CalculatedColumn> _columns;

    private ProcessesAndPortsTableLayout(int width, IReadOnlyList<CalculatedColumn> columns)
    {
        Width = width;
        _columns = columns;
    }

    public int Width { get; }
    public IReadOnlyList<CalculatedColumn> Columns => _columns;

    public static ProcessesAndPortsTableLayout Calculate(int width)
    {
        width = Math.Max(0, width);
        var visible = Definitions.Select(column => new ColumnState(column, column.PreferredWidth)).ToList();
        ColumnState remote = visible[^1];
        remote.Width = Math.Max(0, width - Footprint(visible.Take(visible.Count - 1)) - SeparatorWidth);

        Shrink(visible, "Remote", width);
        Shrink(visible, "Local address", width);
        Shrink(visible, "Process", width);
        Hide(visible, "Remote", width);
        Hide(visible, "State", width);
        Hide(visible, "Local address", width);
        Hide(visible, "Process", width);
        Hide(visible, "Port", width);
        Hide(visible, "Proto", width);
        Hide(visible, "PID", width);

        int offset = 0;
        var columns = new List<CalculatedColumn>(visible.Count);
        foreach (ColumnState state in visible.Where(state => state.Visible))
        {
            columns.Add(new CalculatedColumn(state.Definition, offset, state.Width));
            offset += state.Width + SeparatorWidth;
        }
        return new(width, columns);
    }

    public string FormatHeader() => Format(column => column.Definition.Header);

    public string FormatRow(ProcessesAndPortsRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Format(column => column.Definition.Value(row));
    }

    private string Format(Func<CalculatedColumn, string> value)
    {
        if (Width == 0)
            return string.Empty;

        var text = new System.Text.StringBuilder(Width);
        foreach (CalculatedColumn column in Columns)
        {
            if (text.Length > 0)
                text.Append(' ', SeparatorWidth);
            text.Append(FormatCell(value(column), column.Width, column.Definition.Alignment));
        }
        return ConsoleTextMetrics.FitToCells(text.ToString(), Width);
    }

    private static void Shrink(List<ColumnState> columns, string header, int availableWidth)
    {
        ColumnState column = columns.Single(column => column.Definition.Header == header);
        int excess = Math.Max(0, Footprint(columns.Where(column => column.Visible)) - availableWidth);
        column.Width = Math.Max(column.Definition.MinimumWidth, column.Width - excess);
    }

    private static void Hide(List<ColumnState> columns, string header, int availableWidth)
    {
        if (Footprint(columns.Where(column => column.Visible)) <= availableWidth)
            return;

        ColumnState column = columns.Single(column => column.Definition.Header == header);
        column.Visible = false;
    }

    private static int Footprint(IEnumerable<ColumnState> columns)
    {
        ColumnState[] visible = columns.Where(column => column.Visible).ToArray();
        return visible.Sum(column => column.Width) + Math.Max(0, visible.Length - 1) * SeparatorWidth;
    }

    private static string FormatCell(string value, int width, TableTextAlignment alignment)
    {
        string clipped = Clip(value, width);
        int padding = width - ConsoleTextMetrics.GetCellWidth(clipped);
        return alignment == TableTextAlignment.Right
            ? new string(' ', padding) + clipped
            : clipped + new string(' ', padding);
    }

    private static string Clip(string value, int width)
    {
        if (width <= 0)
            return string.Empty;
        if (ConsoleTextMetrics.GetCellWidth(value) <= width)
            return value;
        if (width == 1)
            return "…";
        return ConsoleTextMetrics.TruncateToCells(value, width - 1) + "…";
    }

    private static string State(ProcessNetworkEndpoint endpoint) => endpoint.Protocol == NetworkTransportProtocol.Udp
        ? "-"
        : endpoint.TcpState == TcpState.Listen ? "Listen" : endpoint.TcpState?.ToString() ?? string.Empty;

    private static string Address(System.Net.IPAddress address, int? port = null) => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"[{address}]{(port is null ? "" : $":{port}")}"
        : $"{address}{(port is null ? "" : $":{port}")}";

    private static string DisplayName(ProcessSnapshot process) => process.Name ?? process.MetadataStatus switch
    {
        ProcessMetadataStatus.AccessDenied => "<access denied>",
        ProcessMetadataStatus.Exited => "<process exited>",
        _ => "<unavailable>",
    };

    private sealed class ColumnState(ProcessesAndPortsColumn definition, int width)
    {
        public ProcessesAndPortsColumn Definition { get; } = definition;
        public int Width { get; set; } = width;
        public bool Visible { get; set; } = true;
    }
}
