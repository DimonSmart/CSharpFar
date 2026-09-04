using CSharpFar.App.Panels;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class FileUsageRenderer(IUiCanvas canvas, CSharpFarPalette palette,
    Func<HoverMarqueeRegistration, string>? renderHoverMarquee = null)
{
    private readonly CellStyle _normal = CSharpFarPaletteStyles.FileUsageNormal(palette);
    private readonly CellStyle _border = new(palette.PanelBorderActiveFg, palette.PanelBackground);
    private readonly CellStyle _title = new(palette.PanelTitleFocusedFg, palette.PanelBackground);

    public ApplicationFileUsageFrame Render(Rect bounds, FileUsagePanelController state)
    {
        canvas.FillRegion(bounds, _normal);
        canvas.DrawDoubleBox(bounds, _border);
        const string title = " File Usage ";
        canvas.Write(bounds.X + Math.Max(0, (bounds.Width - title.Length) / 2), bounds.Y, title, _title);
        int x = bounds.X + 1, y = bounds.Y + 1, width = Math.Max(0, bounds.Width - 2), rows = Math.Max(0, bounds.Height - 2);
        FileUsageLayout layout = FileUsagePresentation.Build(state.Snapshot, state.IsInspecting, state.Message,
            state.SelectedOwnerIndex, state.CanUnlock, width, rows);
        state.NormalizeSelection(layout.Body.Count(row => row.OwnerIndex is not null));
        layout = FileUsagePresentation.Build(state.Snapshot, state.IsInspecting, state.Message,
            state.SelectedOwnerIndex, state.CanUnlock, width, rows);
        var hits = new List<ApplicationFileUsageOwnerHit>();
        for (int i = 0; i < layout.Body.Count; i++)
        {
            DrawRow(x, y + i, width, layout.Body[i], false, state);
            if (layout.Body[i].OwnerIndex is int ownerIndex) hits.Add(new(new Rect(x, y + i, width, 1), ownerIndex));
        }
        if (layout.Action is not null) DrawRow(x, y + rows - 1, width, layout.Action, true, state);
        return new(bounds, hits);
    }

    private void DrawRow(int x, int y, int width, FileUsageRow row, bool action, FileUsagePanelController state)
    {
        canvas.Write(x, y, new string(' ', width), action ? CSharpFarPaletteStyles.FileUsageActionLabel(palette) : _normal);
        int offset = 0;
        foreach (FileUsageRun run in row.Runs)
        {
            int available = Math.Max(0, width - offset);
            string text = FileUsagePresentation.Ellipsize(run.Text, available);
            if (run.Marquee is { } marquee && renderHoverMarquee is not null)
            {
                int visibleCells = Math.Min(available, marquee.VisibleCellWidth);
                if (visibleCells > 0 && ConsoleTextMetrics.GetCellWidth(marquee.FullText) > visibleCells)
                {
                    object ownerIdentity = marquee.OwnerIndex >= 0 && state.Snapshot is { } snapshot && marquee.OwnerIndex < snapshot.Owners.Count
                        ? snapshot.Owners[marquee.OwnerIndex].Process.Identity ?? (object)(snapshot.Owners[marquee.OwnerIndex].Process.ProcessId, snapshot.Owners[marquee.OwnerIndex].Process.Name)
                        : state.SelectedOwnerIndex >= 0 && state.Snapshot is { } selected && state.SelectedOwnerIndex < selected.Owners.Count
                            ? selected.Owners[state.SelectedOwnerIndex].Process.Identity ?? (object)(selected.Owners[state.SelectedOwnerIndex].Process.ProcessId, selected.Owners[state.SelectedOwnerIndex].Process.Name)
                            : "none";
                    var registration = new HoverMarqueeRegistration(
                        new FileUsageMarqueeIdentity(state.PresentationRevision, ownerIdentity, marquee.Detail),
                        marquee.FullText, new Rect(x + offset, y, visibleCells, 1), visibleCells);
                    text = PadToCells(renderHoverMarquee(registration), visibleCells);
                }
            }
            canvas.Write(x + offset, y, text, Style(run.Style)); offset += ConsoleTextMetrics.GetCellWidth(text);
            if (offset >= width) break;
        }
    }

    private CellStyle Style(FileUsageStyleRole role) => role switch
    {
        FileUsageStyleRole.Secondary => CSharpFarPaletteStyles.FileUsageSecondary(palette),
        FileUsageStyleRole.Blocked => CSharpFarPaletteStyles.FileUsageBlocked(palette),
        FileUsageStyleRole.ReasonHeading => CSharpFarPaletteStyles.FileUsageReasonHeading(palette),
        FileUsageStyleRole.ReasonText => CSharpFarPaletteStyles.FileUsageReasonText(palette),
        FileUsageStyleRole.SelectedOwner => CSharpFarPaletteStyles.FileUsageSelectedOwner(palette),
        FileUsageStyleRole.ActionKey => CSharpFarPaletteStyles.FileUsageActionKey(palette),
        FileUsageStyleRole.ActionLabel => CSharpFarPaletteStyles.FileUsageActionLabel(palette),
        _ => _normal,
    };

    private static string PadToCells(string text, int width)
    {
        int cells = ConsoleTextMetrics.GetCellWidth(text);
        return cells >= width ? text : text + new string(' ', width - cells);
    }
}

internal sealed record FileUsageMarqueeIdentity(long Revision, object Owner, string Detail);
