using CSharpFar.Platform.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal enum FileUsageStyleRole { Normal, Secondary, Blocked, ReasonHeading, ReasonText, SelectedOwner, ActionKey, ActionLabel }
internal sealed record FileUsageMarquee(string FullText, int VisibleCellWidth, int OwnerIndex, string Detail);
internal sealed record FileUsageRun(string Text, FileUsageStyleRole Style, FileUsageMarquee? Marquee = null);
internal sealed record FileUsageRow(IReadOnlyList<FileUsageRun> Runs, int? OwnerIndex = null)
{
    public string Text => string.Concat(Runs.Select(run => run.Text));
}
internal sealed record FileUsageLayout(IReadOnlyList<FileUsageRow> Body, FileUsageRow? Action);

internal static class FileUsagePresentation
{
    public static FileUsageLayout Build(FileUsageSnapshot? snapshot, bool inspecting, string? message,
        int selectedOwner, bool canUnlock, int width, int height)
    {
        width = Math.Max(0, width); height = Math.Max(0, height);
        FileUsageRow? action = canUnlock && height > 0 ? Row(Run("Ctrl+U", FileUsageStyleRole.ActionKey), Run(" Unlock owner", FileUsageStyleRole.ActionLabel)) : null;
        int bodyHeight = Math.Max(0, height - (action is null ? 0 : 1));
        var diagnostic = new List<FileUsageRow>();
        if (inspecting) diagnostic.Add(Row(Run("Inspecting...", FileUsageStyleRole.Secondary)));
        if (snapshot is not null)
        {
            diagnostic.Add(LabelValue("State: ", FormatState(snapshot.State), snapshot.State == FileUsageState.Blocked ? FileUsageStyleRole.Blocked : FileUsageStyleRole.Normal));
            foreach (FileUsageOperation operation in Enum.GetValues<FileUsageOperation>())
            {
                FileUsageProbe? probe = snapshot.Probes.FirstOrDefault(p => p.Operation == operation);
                string value = probe?.Status == FileUsageProbeStatus.Blocked ? "BLOCKED" : probe?.Status.ToString() ?? "Unknown";
                diagnostic.Add(LabelValue($"{operation}: ", value, probe?.Status == FileUsageProbeStatus.Blocked ? FileUsageStyleRole.Blocked : FileUsageStyleRole.Secondary));
                if (!string.IsNullOrWhiteSpace(probe?.Error?.Message)) diagnostic.AddRange(WrapReason(probe.Error.Message, width));
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Error?.Message)) diagnostic.AddRange(WrapReason(snapshot.Error.Message, width));
        }
        if (!string.IsNullOrWhiteSpace(message) && message != snapshot?.Error?.Message) diagnostic.AddRange(WrapReason(message, width));

        var result = diagnostic.Take(bodyHeight).ToList();
        if (snapshot is not null && result.Count < bodyHeight)
        {
            result.Add(Row(Run(snapshot.Owners.Count == 0 ? "Owners: none" : "Owners:", FileUsageStyleRole.Normal)));
            int visibleOwners = Math.Min(Math.Max(0, bodyHeight - result.Count), snapshot.Owners.Count);
            for (int i = 0; i < visibleOwners; i++)
            {
                FileUsageOwnerEntry owner = snapshot.Owners[i]; string pid = $"PID {owner.Process.ProcessId}";
                int nameWidth = Math.Max(1, width - pid.Length - 3); string name = Ellipsize(owner.Process.Name ?? "Unknown", nameWidth);
                string text = width >= pid.Length + 4 ? $"  {name.PadRight(nameWidth)} {pid}" : Ellipsize($"  {name} {pid}", width);
                FileUsageStyleRole ownerStyle = i == selectedOwner ? FileUsageStyleRole.SelectedOwner : FileUsageStyleRole.Normal;
                if (width >= pid.Length + 4)
                {
                    string fullName = owner.Process.Name ?? "Unknown";
                    FileUsageMarquee? marquee = ConsoleTextMetrics.GetCellWidth(fullName) > nameWidth
                        ? new(fullName, nameWidth, i, "Name") : null;
                    result.Add(new([Run("  ", ownerStyle), Run(PadToCells(name, nameWidth), ownerStyle, marquee),
                        Run(" " + pid, ownerStyle)], i));
                }
                else
                    result.Add(new([Run(text, ownerStyle)], i));
            }
            if (selectedOwner >= 0 && selectedOwner < visibleOwners && result.Count < bodyHeight)
            {
                FileUsageOwnerEntry owner = snapshot.Owners[selectedOwner];
                AddDetail(result, bodyHeight, width, "Type", $"{owner.Kind}{(owner.ServiceName is null ? "" : " / " + owner.ServiceName)}");
                AddDetail(result, bodyHeight, width, "Path", owner.Process.ExecutablePath ?? "Unavailable");
                AddDetail(result, bodyHeight, width, "Started", owner.Process.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unavailable");
                AddDetail(result, bodyHeight, width, "Restartable", owner.IsRestartable?.ToString() ?? "Unknown");
                if (owner.MetadataUnavailableReason is not null || owner.Process.MetadataStatus != ProcessMetadataStatus.Available)
                    AddDetail(result, bodyHeight, width, "Metadata", owner.MetadataUnavailableReason ?? owner.Process.MetadataStatus.ToString());
            }
        }
        return new(result, action);
    }

    public static IReadOnlyList<FileUsageRow> WrapReason(string message, int width)
    {
        const string prefix = "  Reason: "; string continuation = new(' ', prefix.Length);
        var rows = new List<FileUsageRow>(); string[] words = message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); int index = 0;
        do
        {
            string indent = rows.Count == 0 ? prefix : continuation; int available = Math.Max(0, width - indent.Length);
            var line = new List<string>(); int used = 0;
            while (index < words.Length)
            {
                string word = words[index];
                if (word.Length > available && line.Count == 0) { line.Add(Ellipsize(word, available)); index++; break; }
                int needed = word.Length + (line.Count == 0 ? 0 : 1); if (used + needed > available) break;
                line.Add(word); used += needed; index++;
            }
            string text = string.Join(' ', line);
            rows.Add(rows.Count == 0 ? Row(Run(prefix, FileUsageStyleRole.ReasonHeading), Run(text, FileUsageStyleRole.ReasonText)) : Row(Run(continuation, FileUsageStyleRole.ReasonHeading), Run(text, FileUsageStyleRole.ReasonText)));
        } while (index < words.Length);
        return rows;
    }

    private static void AddDetail(List<FileUsageRow> rows, int height, int width, string label, string value)
    {
        if (rows.Count >= height) return; string prefix = $"  {label}: ";
        int valueWidth = Math.Max(0, width - prefix.Length);
        FileUsageMarquee? marquee = ConsoleTextMetrics.GetCellWidth(value) > valueWidth
            ? new(value, valueWidth, -1, label) : null;
        rows.Add(Row(Run(prefix, FileUsageStyleRole.Secondary),
            Run(Ellipsize(value, valueWidth), FileUsageStyleRole.Normal, marquee)));
    }
    private static FileUsageRow LabelValue(string label, string value, FileUsageStyleRole role) => Row(Run(label, FileUsageStyleRole.Secondary), Run(value, role));
    private static FileUsageRun Run(string text, FileUsageStyleRole role, FileUsageMarquee? marquee = null) =>
        new(text, role, marquee);
    private static FileUsageRow Row(params FileUsageRun[] runs) => new(runs);
    internal static string Ellipsize(string value, int width)
    {
        const string ellipsis = "…";
        int cells = ConsoleTextMetrics.GetCellWidth(value);
        return width <= 0 ? "" : cells <= width ? value : width == 1 ? ellipsis :
            ConsoleTextMetrics.SliceToCells(value, 0, width - 1) + ellipsis;
    }
    private static string PadToCells(string value, int width)
    {
        int cells = ConsoleTextMetrics.GetCellWidth(value);
        return cells >= width ? value : value + new string(' ', width - cells);
    }
    private static string FormatState(FileUsageState state) => state switch { FileUsageState.Blocked => "BLOCKED", FileUsageState.InUse => "In Use", _ => state.ToString() };
}
