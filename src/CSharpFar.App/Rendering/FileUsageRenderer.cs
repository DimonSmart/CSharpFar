using CSharpFar.App.Panels;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Platform.Abstractions;

namespace CSharpFar.App.Rendering;

internal sealed class FileUsageRenderer(IUiCanvas canvas, ConsolePalette palette)
{
    private readonly CellStyle _normal = new(palette.NormalFileFg, palette.PanelBackground);
    private readonly CellStyle _border = new(palette.PanelBorderActiveFg, palette.PanelBackground);
    private readonly CellStyle _title = new(palette.PanelTitleFocusedFg, palette.PanelBackground);

    public ApplicationFileUsageFrame Render(Rect bounds, FileUsagePanelController state)
    {
        canvas.FillRegion(bounds, _normal);
        canvas.DrawDoubleBox(bounds, _border);
        const string title = " File Usage ";
        canvas.Write(bounds.X + Math.Max(0, (bounds.Width - title.Length) / 2), bounds.Y, title, _title);
        int x = bounds.X + 1, y = bounds.Y + 1, width = Math.Max(0, bounds.Width - 2), rows = Math.Max(0, bounds.Height - 2);
        var lines = new List<string>();
        if (state.IsInspecting) lines.Add("Inspecting...");
        if (state.Snapshot is { } snapshot)
        {
            lines.Add($"State: {FormatState(snapshot.State)}");
            foreach (FileUsageOperation operation in Enum.GetValues<FileUsageOperation>())
            {
                FileUsageProbe? probe = snapshot.Probes.FirstOrDefault(p => p.Operation == operation);
                lines.Add($"{operation}: {probe?.Status.ToString() ?? "Unknown"}{(probe?.Error is null ? "" : " - " + probe.Error.Message)}");
            }
            lines.Add(string.Empty);
            lines.Add(snapshot.Owners.Count == 0 ? "Owners: none" : "Owners:");
        }
        if (!string.IsNullOrWhiteSpace(state.Message)) lines.Add(state.Message!);

        int ownerStart = lines.Count;
        var hits = new List<ApplicationFileUsageOwnerHit>();
        if (state.Snapshot is { } current)
        {
            int detailReserve = state.SelectedOwnerIndex >= 0 ? 6 : 0;
            int visibleOwners = Math.Max(0, rows - ownerStart - detailReserve);
            state.NormalizeSelection(visibleOwners);
            for (int i = 0; i < Math.Min(visibleOwners, current.Owners.Count); i++)
            {
                var owner = current.Owners[i];
                lines.Add($"{(i == state.SelectedOwnerIndex ? '>' : ' ')} {owner.Process.Name ?? "Unknown"}  PID {owner.Process.ProcessId}");
                hits.Add(new(new Rect(x, y + lines.Count - 1, width, 1), i));
            }
            if (state.SelectedOwnerIndex >= 0 && state.SelectedOwnerIndex < current.Owners.Count)
            {
                FileUsageOwnerEntry owner = current.Owners[state.SelectedOwnerIndex];
                lines.Add(string.Empty);
                lines.Add($"Type: {owner.Kind}{(owner.ServiceName is null ? "" : " / " + owner.ServiceName)}");
                lines.Add($"Path: {owner.Process.ExecutablePath ?? "Unavailable"}");
                lines.Add($"Started: {owner.Process.StartedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unavailable"}");
                lines.Add($"Restartable: {owner.IsRestartable?.ToString() ?? "Unknown"}");
                if (owner.MetadataUnavailableReason is not null || owner.Process.MetadataStatus != ProcessMetadataStatus.Available)
                    lines.Add($"Metadata: {owner.MetadataUnavailableReason ?? owner.Process.MetadataStatus.ToString()}");
                if (state.CanUnlock) lines.Add("Ctrl+U: Unlock owner");
            }
        }
        for (int i = 0; i < rows; i++)
        {
            string line = i < lines.Count ? lines[i] : string.Empty;
            if (line.Length > width) line = width > 1 ? line[..(width - 1)] + "…" : line[..width];
            canvas.Write(x, y + i, line.PadRight(width), _normal);
        }
        return new(bounds, hits);
    }

    private static string FormatState(FileUsageState state) => state switch
    {
        FileUsageState.InUse => "In Use",
        _ => state.ToString(),
    };
}
