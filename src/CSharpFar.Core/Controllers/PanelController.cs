using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Controllers;

public sealed class PanelController
{
    private readonly IFileSystemService _fs;

    public PanelController(IFileSystemService fs) => _fs = fs;

    /// <summary>Loads a directory into the panel state. Throws on access errors.</summary>
    public void LoadDirectory(FilePanelState state, string path)
    {
        var items = _fs.ReadDirectory(path);
        state.CurrentDirectory = path;
        state.Items.Clear();
        state.Items.AddRange(items);
        state.SelectedPaths.Clear();
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
        ApplySort(state);
    }

    /// <summary>
    /// Navigates to the parent directory and positions the cursor
    /// on the subdirectory we came from.
    /// </summary>
    public void GoToParent(FilePanelState state, int visibleRows)
    {
        var info = new DirectoryInfo(state.CurrentDirectory);
        if (info.Parent == null) return;

        string childName = info.Name;
        LoadDirectory(state, info.Parent.FullName);

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

    public void MoveToFirst(FilePanelState state)
    {
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
    }

    public void MoveToLast(FilePanelState state, int visibleRows)
    {
        state.CursorIndex = Math.Max(0, state.Items.Count - 1);
        EnsureVisible(state, visibleRows);
    }

    /// <summary>
    /// Reloads the current directory, preserving cursor position by name.
    /// Use after shell commands that may have changed directory contents.
    /// </summary>
    public void RefreshDirectory(FilePanelState state, int visibleRows)
    {
        string? cursorName = CurrentItem(state)?.Name;
        var selectedPaths = state.SelectedPaths.ToList();
        LoadDirectory(state, state.CurrentDirectory);

        var availablePaths = state.Items
            .Where(i => !i.IsParentDirectory)
            .Select(i => i.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string selectedPath in selectedPaths)
        {
            if (availablePaths.Contains(selectedPath))
                state.SelectedPaths.Add(selectedPath);
        }

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

    /// <summary>
    /// Moves the cursor to the item whose name matches <paramref name="name"/> (case-insensitive).
    /// Does nothing if not found.
    /// </summary>
    public void SetCursorByName(FilePanelState state, string name, int visibleRows)
    {
        int idx = state.Items.FindIndex(
            i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) { state.CursorIndex = idx; EnsureVisible(state, visibleRows); }
    }

    /// <summary>Returns the item currently under the cursor, or null.</summary>
    public FilePanelItem? CurrentItem(FilePanelState state) =>
        state.CursorIndex >= 0 && state.CursorIndex < state.Items.Count
            ? state.Items[state.CursorIndex]
            : null;

    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>Toggles selection of the current item and advances the cursor.</summary>
    public void ToggleSelection(FilePanelState state, int visibleRows)
    {
        var item = CurrentItem(state);
        if (item is not null && !item.IsParentDirectory)
        {
            if (!state.SelectedPaths.Remove(item.FullPath))
                state.SelectedPaths.Add(item.FullPath);
        }
        MoveCursor(state, +1, visibleRows);
    }

    /// <summary>
    /// Selects all non-parent items, or clears all selections if everything is already selected.
    /// </summary>
    public void ToggleSelectAll(FilePanelState state)
    {
        var selectable = state.Items.Where(i => !i.IsParentDirectory).ToList();
        bool allSelected = selectable.Count > 0 &&
                           selectable.All(i => state.SelectedPaths.Contains(i.FullPath));

        state.SelectedPaths.Clear();
        if (!allSelected)
            foreach (var item in selectable)
                state.SelectedPaths.Add(item.FullPath);
    }

    /// <summary>Inverts selection of all non-parent items without moving the cursor.</summary>
    public void InvertSelection(FilePanelState state)
    {
        foreach (var item in state.Items.Where(i => !i.IsParentDirectory))
        {
            if (!state.SelectedPaths.Remove(item.FullPath))
                state.SelectedPaths.Add(item.FullPath);
        }
    }

    // ── Sorting ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Changes the active sort mode. Calling with the current mode toggles sort direction.
    /// Preserves the cursor position by name.
    /// </summary>
    public void SetSortMode(FilePanelState state, SortMode mode, int visibleRows)
    {
        if (state.SortMode == mode)
            state.SortDescending = !state.SortDescending;
        else
        {
            state.SortMode = mode;
            state.SortDescending = false;
        }

        string? cursorName = CurrentItem(state)?.Name;
        ApplySort(state);

        if (cursorName is not null)
        {
            int idx = state.Items.FindIndex(
                i => string.Equals(i.Name, cursorName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) { state.CursorIndex = idx; EnsureVisible(state, visibleRows); }
        }
    }

    private static void ApplySort(FilePanelState state)
    {
        var parent = state.Items.FirstOrDefault(i => i.IsParentDirectory);
        var dirs   = state.Items.Where(i => i.IsDirectory && !i.IsParentDirectory).ToList();
        var files  = state.Items.Where(i => !i.IsDirectory).ToList();

        var sortedDirs  = SortItems(dirs,  state.SortMode).ToList();
        var sortedFiles = SortItems(files, state.SortMode).ToList();

        if (state.SortDescending)
        {
            sortedDirs.Reverse();
            sortedFiles.Reverse();
        }

        state.Items.Clear();
        if (parent is not null) state.Items.Add(parent);
        state.Items.AddRange(sortedDirs);
        state.Items.AddRange(sortedFiles);
    }

    private static IEnumerable<FilePanelItem> SortItems(IEnumerable<FilePanelItem> items, SortMode mode) =>
        mode switch
        {
            SortMode.Name          => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Extension     => items
                                         .OrderBy(i => Path.GetExtension(i.Name), StringComparer.OrdinalIgnoreCase)
                                         .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Size          => items.OrderBy(i => i.Size ?? 0),
            SortMode.LastWriteTime => items.OrderBy(i => i.LastWriteTime),
            _                      => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };

    private static void EnsureVisible(FilePanelState state, int visibleRows)
    {
        if (visibleRows <= 0) return;
        if (state.CursorIndex < state.ScrollOffset)
            state.ScrollOffset = state.CursorIndex;
        else if (state.CursorIndex >= state.ScrollOffset + visibleRows)
            state.ScrollOffset = state.CursorIndex - visibleRows + 1;
        state.ScrollOffset = Math.Max(0, state.ScrollOffset);
    }
}
