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
        state.CursorIndex = 0;
        state.ScrollOffset = 0;
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

    /// <summary>Returns the item currently under the cursor, or null.</summary>
    public FilePanelItem? CurrentItem(FilePanelState state) =>
        state.CursorIndex >= 0 && state.CursorIndex < state.Items.Count
            ? state.Items[state.CursorIndex]
            : null;

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
