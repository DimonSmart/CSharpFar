using CSharpFar.App.Rendering;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using AppSettings = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.HitTesting;

/// <summary>
/// Static helper that maps absolute console coordinates to panel elements.
/// Uses the same layout calculations as the panel renderers.
/// </summary>
public static class PanelHitTester
{
    /// <summary>
    /// Returns the item index at (x, y) within the panel content area, or null if not hit.
    /// </summary>
    public static int? HitTestItem(
        int                                x,
        int                                y,
        Rect                               bounds,
        FilePanelState                     state,
        PanelViewMode                      viewMode,
        AppSettings.PanelOptionsSettings?  options = null)
    {
        // Must be inside inner content area (inside border)
        if (x <= bounds.X || x >= bounds.Right - 1) return null;
        if (y <= bounds.Y || y >= bounds.Bottom - 1) return null;
        if (options?.ShowScrollbar == true && x == bounds.Right - 2) return null;

        if (viewMode == PanelViewMode.BriefTwoColumns)
            return HitTestBrief(x, y, bounds, state, options);

        return HitTestFull(x, y, bounds, state, options);
    }

    private static int? HitTestFull(
        int x, int y,
        Rect bounds, FilePanelState state,
        AppSettings.PanelOptionsSettings? options)
    {
        int listTop  = bounds.Y + 1;
        int visRows  = PanelRenderer.VisibleRows(bounds, options);
        int listBottom = listTop + visRows - 1;

        if (y < listTop || y > listBottom) return null;

        int row = y - listTop;
        int idx = state.ScrollOffset + row;
        return idx >= 0 && idx < state.Items.Count ? idx : null;
    }

    private static int? HitTestBrief(
        int x, int y,
        Rect bounds, FilePanelState state,
        AppSettings.PanelOptionsSettings? options)
    {
        int contentTop  = bounds.Y + 2;  // after header row
        int rowsPerCol  = BriefTwoColumnsPanelRenderer.RowsPerColumn(bounds, options);
        int contentBottom = contentTop + rowsPerCol - 1;

        if (y < contentTop || y > contentBottom) return null;

        int row = y - contentTop;
        int innerWidth = bounds.Width - 2;
        int sepOffset  = innerWidth / 2;
        int sep        = bounds.X + 1 + sepOffset;

        int idx;
        if (x < sep)
            idx = state.ScrollOffset + row;
        else if (x > sep)
            idx = state.ScrollOffset + rowsPerCol + row;
        else
            return null; // on separator char

        return idx >= 0 && idx < state.Items.Count ? idx : null;
    }
}
