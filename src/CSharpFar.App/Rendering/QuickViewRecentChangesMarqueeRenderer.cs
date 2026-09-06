using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal static class QuickViewRecentChangesMarqueeRenderer
{
    public static void Render(
        IUiCanvas canvas,
        ApplicationQuickViewFrame? quickView,
        CSharpFarPalette palette,
        Func<HoverMarqueeRegistration, string>? renderHoverMarquee)
    {
        if (renderHoverMarquee is null ||
            quickView?.RecentChangesFrame is not { } frame ||
            quickView.RecentChanges is not { } recentChanges ||
            frame.ContentBounds.Width <= 0)
            return;

        var normalStyle = new CellStyle(palette.NormalFileFg, palette.PanelBackground);
        var selectedStyle = new CellStyle(palette.PanelTitleFocusedFg, palette.PanelBackground);
        int visibleCount = Math.Min(frame.ViewportRows, Math.Max(0, frame.ItemCount - frame.ScrollTop));

        for (int row = 0; row < visibleCount; row++)
        {
            int index = frame.ScrollTop + row;
            DirectoryChange change = recentChanges.State.Items[index];
            var (prefix, path) = FormatChangeParts(change);
            int prefixWidth = ConsoleTextMetrics.GetCellWidth(prefix);
            int pathWidth = frame.ContentBounds.Width - prefixWidth;
            if (pathWidth <= 0 || ConsoleTextMetrics.GetCellWidth(path) <= pathWidth)
                continue;

            var registration = new HoverMarqueeRegistration(
                new QuickViewRecentChangeMarqueeIdentity(change.Id),
                path,
                new Rect(frame.ContentBounds.X + prefixWidth, frame.ContentBounds.Y + row, pathWidth, 1),
                pathWidth);
            string text = ConsoleTextMetrics.FitToCells(renderHoverMarquee(registration), pathWidth);
            canvas.Write(
                registration.Bounds.X,
                registration.Bounds.Y,
                text,
                index == frame.SelectedIndex ? selectedStyle : normalStyle);
        }
    }

    private static (string Prefix, string Path) FormatChangeParts(DirectoryChange change)
    {
        string marker = change.Kind switch
        {
            DirectoryChangeKind.Created => "+",
            DirectoryChangeKind.Changed when change.RepeatCount > 1 => $"M×{change.RepeatCount}",
            DirectoryChangeKind.Changed => "M",
            DirectoryChangeKind.Deleted => "-",
            _ => "R",
        };
        string path = change.Kind == DirectoryChangeKind.Renamed
            ? $"{change.OldRelativePath} -> {change.RelativePath}"
            : change.RelativePath;
        return ($"{change.Timestamp.ToLocalTime():HH:mm:ss}  {marker}  ", path);
    }
}

internal sealed record QuickViewRecentChangeMarqueeIdentity(long ChangeId);
