using CSharpFar.App.Panels;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal sealed class FileUsageRenderer(IUiCanvas canvas, ConsolePalette palette)
{
    private readonly CellStyle _normal = PaletteStyles.FileUsageNormal(palette);
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
            DrawRow(x, y + i, width, layout.Body[i], false);
            if (layout.Body[i].OwnerIndex is int ownerIndex) hits.Add(new(new Rect(x, y + i, width, 1), ownerIndex));
        }
        if (layout.Action is not null) DrawRow(x, y + rows - 1, width, layout.Action, true);
        return new(bounds, hits);
    }

    private void DrawRow(int x, int y, int width, FileUsageRow row, bool action)
    {
        canvas.Write(x, y, new string(' ', width), action ? PaletteStyles.FileUsageActionLabel(palette) : _normal);
        int offset = 0;
        foreach (FileUsageRun run in row.Runs)
        {
            string text = FileUsagePresentation.Ellipsize(run.Text, Math.Max(0, width - offset));
            canvas.Write(x + offset, y, text, Style(run.Style)); offset += text.Length;
            if (offset >= width) break;
        }
    }

    private CellStyle Style(FileUsageStyleRole role) => role switch
    {
        FileUsageStyleRole.Secondary => PaletteStyles.FileUsageSecondary(palette),
        FileUsageStyleRole.Blocked => PaletteStyles.FileUsageBlocked(palette),
        FileUsageStyleRole.ReasonHeading => PaletteStyles.FileUsageReasonHeading(palette),
        FileUsageStyleRole.ReasonText => PaletteStyles.FileUsageReasonText(palette),
        FileUsageStyleRole.SelectedOwner => PaletteStyles.FileUsageSelectedOwner(palette),
        FileUsageStyleRole.ActionKey => PaletteStyles.FileUsageActionKey(palette),
        FileUsageStyleRole.ActionLabel => PaletteStyles.FileUsageActionLabel(palette),
        _ => _normal,
    };
}
