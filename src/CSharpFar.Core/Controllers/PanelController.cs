using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Controllers;

public sealed class PanelController
{
    private readonly IPanelViewBuilder _viewBuilder;

    public PanelController(IPanelViewBuilder viewBuilder) => _viewBuilder = viewBuilder;

    public void LoadDirectory(
        FilePanelState state,
        string path,
        AppSettings.PanelOptionsSettings? options = null)
    {
        options ??= new AppSettings.PanelOptionsSettings();
        var view = _viewBuilder.Build(new PanelViewRequest
        {
            DirectoryPath  = path,
            Options        = options,
            SortMode       = state.SortMode,
            SortDescending = state.SortDescending,
            SelectedPaths  = s_emptySet,
        });
        state.CurrentDirectory = path;
        state.Items.Clear();
        state.Items.AddRange(view.Items);
        state.Summary          = view.Summary;
        state.AutoRefreshState = view.AutoRefreshState;
        state.ProviderCapabilities = PanelProviderCapabilities.LocalFileSystem;
        state.DisplayTitle = null;
        state.ShowCurrentItemFullPath = false;
        state.SearchRequest = null;
        state.SearchWasCancelled = false;
        state.SelectedPaths.Clear();
        state.CursorIndex  = 0;
        state.ScrollOffset = 0;
    }

    public void GoToParent(
        FilePanelState state,
        int visibleRows,
        AppSettings.PanelOptionsSettings? options = null)
    {
        var info = new DirectoryInfo(state.CurrentDirectory);
        if (info.Parent == null) return;

        string childName = info.Name;
        LoadDirectory(state, info.Parent.FullName, options);

        int idx = state.Items.FindIndex(
            item => string.Equals(item.Name, childName, StringComparison.OrdinalIgnoreCase));

        if (idx >= 0)
        {
            state.CursorIndex = idx;
            EnsureVisible(state, visibleRows);
        }
    }

    public void MoveCursor(FilePanelState state, int delta, int visibleRows)
    {
        if (state.Items.Count == 0) return;
        state.CursorIndex = Math.Clamp(state.CursorIndex + delta, 0, state.Items.Count - 1);
        EnsureVisible(state, visibleRows);
    }

    /// <summary>Sets cursor to a specific index and scrolls it into view.</summary>
    public void SetCursorTo(FilePanelState state, int index, int visibleRows)
    {
        if (state.Items.Count == 0) return;
        state.CursorIndex = Math.Clamp(index, 0, state.Items.Count - 1);
        EnsureVisible(state, visibleRows);
    }

    /// <summary>Scrolls the view without moving the cursor (cursor stays in the new viewport).</summary>
    public void ScrollView(FilePanelState state, int delta, int visibleRows)
    {
        if (state.Items.Count == 0) return;
        state.ScrollOffset = Math.Clamp(
            state.ScrollOffset + delta,
            0,
            Math.Max(0, state.Items.Count - visibleRows));
        // Keep cursor visible in the new viewport
        state.CursorIndex = Math.Clamp(
            state.CursorIndex,
            state.ScrollOffset,
            Math.Max(state.ScrollOffset, state.ScrollOffset + visibleRows - 1));
        state.CursorIndex = Math.Clamp(state.CursorIndex, 0, state.Items.Count - 1);
    }

    public void MoveToFirst(FilePanelState state)
    {
        state.CursorIndex  = 0;
        state.ScrollOffset = 0;
    }

    public void MoveToLast(FilePanelState state, int visibleRows)
    {
        state.CursorIndex = Math.Max(0, state.Items.Count - 1);
        EnsureVisible(state, visibleRows);
    }

    public void MoveCursorByColumn(
        FilePanelState state,
        int direction,
        int rowsPerColumn,
        int columnCount,
        int visibleRows)
    {
        if (state.Items.Count == 0 || direction == 0) return;

        if (rowsPerColumn <= 0 || columnCount <= 1)
        {
            if (direction < 0) MoveToFirst(state);
            else               MoveToLast(state, visibleRows);
            return;
        }

        EnsureVisible(state, visibleRows);

        int relative  = state.CursorIndex - state.ScrollOffset;
        int column    = Math.Clamp(relative / rowsPerColumn, 0, columnCount - 1);
        int lastIndex = state.Items.Count - 1;

        int targetIndex;
        if (direction < 0)
            targetIndex = column == 0 ? 0 : Math.Max(0, state.CursorIndex - rowsPerColumn);
        else
            targetIndex = column >= columnCount - 1
                ? lastIndex
                : Math.Min(lastIndex, state.CursorIndex + rowsPerColumn);

        state.CursorIndex = targetIndex;
        EnsureVisible(state, visibleRows);
    }

    public void RefreshDirectory(
        FilePanelState state,
        int visibleRows,
        AppSettings.PanelOptionsSettings? options = null)
    {
        string? cursorName    = CurrentItem(state)?.Name;
        var     selectedPaths = state.SelectedPaths.ToList();

        options ??= new AppSettings.PanelOptionsSettings();
        var view = _viewBuilder.Build(new PanelViewRequest
        {
            DirectoryPath  = state.CurrentDirectory,
            Options        = options,
            SortMode       = state.SortMode,
            SortDescending = state.SortDescending,
            SelectedPaths  = selectedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase),
        });
        state.Items.Clear();
        state.Items.AddRange(view.Items);
        state.Summary          = view.Summary;
        state.AutoRefreshState = view.AutoRefreshState;

        var availablePaths = state.Items
            .Where(i => !i.IsParentDirectory)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        state.SelectedPaths.Clear();
        foreach (string p in selectedPaths)
            if (availablePaths.Contains(p))
                state.SelectedPaths.Add(p);
        RefreshSelectedSummary(state);

        if (cursorName is not null)
        {
            int idx = state.Items.FindIndex(
                i => string.Equals(i.Name, cursorName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                state.CursorIndex = idx;
                EnsureVisible(state, visibleRows);
            }
        }
    }

    public void SetCursorByName(FilePanelState state, string name, int visibleRows)
    {
        int idx = state.Items.FindIndex(
            i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) { state.CursorIndex = idx; EnsureVisible(state, visibleRows); }
    }

    public FilePanelItem? CurrentItem(FilePanelState state) =>
        state.CursorIndex >= 0 && state.CursorIndex < state.Items.Count
            ? state.Items[state.CursorIndex]
            : null;

    public void ToggleSelection(
        FilePanelState state,
        int visibleRows,
        AppSettings.PanelOptionsSettings? options = null)
    {
        options ??= new AppSettings.PanelOptionsSettings();
        var item = CurrentItem(state);
        if (item is not null && CanSelect(item, options))
        {
            if (!state.SelectedPaths.Remove(item.FullPath))
                state.SelectedPaths.Add(item.FullPath);
            RefreshSelectedSummary(state);
        }
        MoveCursor(state, +1, visibleRows);
    }

    public void ToggleCurrentSelection(
        FilePanelState state,
        AppSettings.PanelOptionsSettings? options = null)
    {
        options ??= new AppSettings.PanelOptionsSettings();
        var item = CurrentItem(state);
        if (item is null || !CanSelect(item, options))
            return;

        if (!state.SelectedPaths.Remove(item.FullPath))
            state.SelectedPaths.Add(item.FullPath);

        RefreshSelectedSummary(state);
    }

    public void ToggleSelectAll(
        FilePanelState state,
        AppSettings.PanelOptionsSettings? options = null)
    {
        options ??= new AppSettings.PanelOptionsSettings();
        var selectable   = state.Items.Where(i => CanSelect(i, options)).ToList();
        bool allSelected = selectable.Count > 0 &&
                           selectable.All(i => state.SelectedPaths.Contains(i.FullPath));

        state.SelectedPaths.Clear();
        if (!allSelected)
            foreach (var item in selectable)
                state.SelectedPaths.Add(item.FullPath);

        RefreshSelectedSummary(state);
    }

    public void InvertSelection(
        FilePanelState state,
        AppSettings.PanelOptionsSettings? options = null)
    {
        options ??= new AppSettings.PanelOptionsSettings();
        foreach (var item in state.Items.Where(i => CanSelect(i, options)))
        {
            if (!state.SelectedPaths.Remove(item.FullPath))
                state.SelectedPaths.Add(item.FullPath);
        }

        RefreshSelectedSummary(state);
    }

    public void SetSortMode(
        FilePanelState state,
        SortMode mode,
        int visibleRows,
        AppSettings.PanelOptionsSettings? options = null)
    {
        if (state.SortMode == mode)
            state.SortDescending = !state.SortDescending;
        else
        {
            state.SortMode       = mode;
            state.SortDescending = false;
        }

        string? cursorName = CurrentItem(state)?.Name;

        options ??= new AppSettings.PanelOptionsSettings();
        var view = _viewBuilder.Build(new PanelViewRequest
        {
            DirectoryPath  = state.CurrentDirectory,
            Options        = options,
            SortMode       = state.SortMode,
            SortDescending = state.SortDescending,
            SelectedPaths  = state.SelectedPaths,
        });
        state.Items.Clear();
        state.Items.AddRange(view.Items);
        state.Summary = view.Summary;

        if (cursorName is not null)
        {
            int idx = state.Items.FindIndex(
                i => string.Equals(i.Name, cursorName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { state.CursorIndex = idx; EnsureVisible(state, visibleRows); }
        }
    }

    public static bool CanSelect(FilePanelItem item, AppSettings.PanelOptionsSettings options)
    {
        if (item.IsParentDirectory)                    return false;
        if (item.IsDirectory && !options.SelectFolders) return false;
        return true;
    }

    private static void EnsureVisible(FilePanelState state, int visibleRows)
    {
        if (visibleRows <= 0) return;
        if (state.CursorIndex < state.ScrollOffset)
            state.ScrollOffset = state.CursorIndex;
        else if (state.CursorIndex >= state.ScrollOffset + visibleRows)
            state.ScrollOffset = state.CursorIndex - visibleRows + 1;
        state.ScrollOffset = Math.Max(0, state.ScrollOffset);
    }

    private static void RefreshSelectedSummary(FilePanelState state)
    {
        int selectedCount = 0;
        long selectedFileSize = 0;

        foreach (var item in state.Items)
        {
            if (item.IsParentDirectory)
                continue;
            if (!state.SelectedPaths.Contains(item.FullPath))
                continue;

            selectedCount++;
            if (!item.IsDirectory)
                selectedFileSize += item.Size ?? 0;
        }

        var summary = state.Summary;
        if (summary is null)
        {
            long totalFileSize = 0;
            int fileCount = 0;
            int directoryCount = 0;

            foreach (var item in state.Items)
            {
                if (item.IsParentDirectory)
                    continue;
                if (item.IsDirectory)
                    directoryCount++;
                else
                {
                    fileCount++;
                    totalFileSize += item.Size ?? 0;
                }
            }

            state.Summary = new PanelSummary
            {
                VisibleItemCount = fileCount + directoryCount,
                FileCount = fileCount,
                DirectoryCount = directoryCount,
                TotalFileSize = totalFileSize,
                SelectedCount = selectedCount,
                SelectedFileSize = selectedFileSize,
            };
            return;
        }

        state.Summary = new PanelSummary
        {
            VisibleItemCount = summary.VisibleItemCount,
            FileCount = summary.FileCount,
            DirectoryCount = summary.DirectoryCount,
            TotalFileSize = summary.TotalFileSize,
            SelectedCount = selectedCount,
            SelectedFileSize = selectedFileSize,
            VolumeSpace = summary.VolumeSpace,
            VolumeSpaceUnavailable = summary.VolumeSpaceUnavailable,
        };
    }

    private static readonly IReadOnlySet<string> s_emptySet =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
