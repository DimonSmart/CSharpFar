using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 5: file selection (Insert / Ctrl+A) and panel sorting (Ctrl+F3–F6).
/// </summary>
public class SelectionSortTests
{
    // ── Setup ──────────────────────────────────────────────────────────────────
    //
    // Directories: Assets (date=2024-03-01), Docs (date=2024-01-01)
    // Files:       a_file.txt (size=50,  date=2024-01-15)
    //              b_file.csv (size=200, date=2024-04-01)
    //              c_file.txt (size=100, date=2024-02-01)
    //
    // After LoadDirectory (default SortMode.Name asc):
    //   [0]=Assets  [1]=Docs  [2]=a_file.txt  [3]=b_file.csv  [4]=c_file.txt

    private const string Root = @"C:\Root";

    private static (PanelController ctrl, FilePanelState state) MakePanel()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "Assets", FullPath = Root + @"\Assets", IsDirectory = true, LastWriteTime = new DateTime(2024, 3, 1) },
            new FilePanelItem { Name = "Docs", FullPath = Root + @"\Docs", IsDirectory = true, LastWriteTime = new DateTime(2024, 1, 1) },
            new FilePanelItem { Name = "a_file.txt", FullPath = Root + @"\a_file.txt", IsDirectory = false, Size = 50, LastWriteTime = new DateTime(2024, 1, 15) },
            new FilePanelItem { Name = "b_file.csv", FullPath = Root + @"\b_file.csv", IsDirectory = false, Size = 200, LastWriteTime = new DateTime(2024, 4, 1) },
            new FilePanelItem { Name = "c_file.txt", FullPath = Root + @"\c_file.txt", IsDirectory = false, Size = 100, LastWriteTime = new DateTime(2024, 2, 1) });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);
        return (ctrl, state);
    }

    // ── Default name sort ─────────────────────────────────────────────────────

    [Fact]
    public void LoadDirectory_DefaultSort_DirsBeforeFiles()
    {
        var (_, state) = MakePanel();

        Assert.True(state.Items[0].IsDirectory);
        Assert.True(state.Items[1].IsDirectory);
        Assert.False(state.Items[2].IsDirectory);
    }

    [Fact]
    public void LoadDirectory_DefaultSort_NameAscending()
    {
        var (_, state) = MakePanel();

        // Dirs ascending: Assets, Docs
        Assert.Equal("Assets", state.Items[0].Name);
        Assert.Equal("Docs", state.Items[1].Name);
        // Files ascending: a_file.txt, b_file.csv, c_file.txt
        Assert.Equal("a_file.txt", state.Items[2].Name);
        Assert.Equal("b_file.csv", state.Items[3].Name);
        Assert.Equal("c_file.txt", state.Items[4].Name);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    [Fact]
    public void ToggleSelection_SelectsCurrentItem()
    {
        var (ctrl, state) = MakePanel();
        state.CursorIndex = 2; // a_file.txt

        ctrl.ToggleSelection(state, 10);

        Assert.Contains(Root + @"\a_file.txt", state.SelectedPaths);
    }

    [Fact]
    public void ToggleSelection_MovesDown()
    {
        var (ctrl, state) = MakePanel();
        state.CursorIndex = 2;

        ctrl.ToggleSelection(state, 10);

        Assert.Equal(3, state.CursorIndex);
    }

    [Fact]
    public void ToggleSelection_DeselecstOnSecondToggle()
    {
        var (ctrl, state) = MakePanel();
        state.CursorIndex = 2; // a_file.txt

        ctrl.ToggleSelection(state, 10);  // select
        state.CursorIndex = 2;
        ctrl.ToggleSelection(state, 10);  // deselect

        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void ToggleSelection_SkipsParentDirectory()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true },
            new FilePanelItem { Name = "file.txt", FullPath = Root + @"\file.txt", IsDirectory = false });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);  // [0]=.. [1]=file.txt

        state.CursorIndex = 0; // on ".."
        ctrl.ToggleSelection(state, 10);

        Assert.Empty(state.SelectedPaths);
        Assert.Equal(1, state.CursorIndex); // cursor still moved
    }

    [Fact]
    public void ToggleSelectAll_SelectsAllNonParentItems()
    {
        var (ctrl, state) = MakePanel();

        ctrl.ToggleSelectAll(state);

        Assert.Equal(5, state.SelectedPaths.Count);
    }

    [Fact]
    public void ToggleSelectAll_DeselectsWhenAllSelected()
    {
        var (ctrl, state) = MakePanel();

        ctrl.ToggleSelectAll(state); // select all
        ctrl.ToggleSelectAll(state); // deselect all

        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void InvertSelection_SelectsAllWhenNoneSelected()
    {
        var (ctrl, state) = MakePanel();

        ctrl.InvertSelection(state);

        Assert.Equal(5, state.SelectedPaths.Count);
    }

    [Fact]
    public void InvertSelection_InvertsPartialSelection()
    {
        var (ctrl, state) = MakePanel();
        state.SelectedPaths.Add(Root + @"\Assets");
        state.SelectedPaths.Add(Root + @"\a_file.txt");

        ctrl.InvertSelection(state);

        Assert.DoesNotContain(Root + @"\Assets", state.SelectedPaths);
        Assert.DoesNotContain(Root + @"\a_file.txt", state.SelectedPaths);
        Assert.Contains(Root + @"\Docs", state.SelectedPaths);
        Assert.Contains(Root + @"\b_file.csv", state.SelectedPaths);
        Assert.Contains(Root + @"\c_file.txt", state.SelectedPaths);
        Assert.Equal(3, state.SelectedPaths.Count);
    }

    [Fact]
    public void InvertSelection_ClearsWhenAllSelected()
    {
        var (ctrl, state) = MakePanel();
        ctrl.ToggleSelectAll(state);

        ctrl.InvertSelection(state);

        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void InvertSelection_SkipsParentDirectory()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true },
            new FilePanelItem { Name = "file.txt", FullPath = Root + @"\file.txt", IsDirectory = false });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);

        ctrl.InvertSelection(state);

        Assert.DoesNotContain(@"C:\", state.SelectedPaths);
        Assert.Contains(Root + @"\file.txt", state.SelectedPaths);
        Assert.Single(state.SelectedPaths);
    }

    [Fact]
    public void InvertSelection_DoesNotMoveCursor()
    {
        var (ctrl, state) = MakePanel();
        state.CursorIndex = 3;
        state.ScrollOffset = 1;

        ctrl.InvertSelection(state);

        Assert.Equal(3, state.CursorIndex);
        Assert.Equal(1, state.ScrollOffset);
    }

    // ── Sorting ───────────────────────────────────────────────────────────────

    [Fact]
    public void SetSortMode_SortsByExtension()
    {
        var (ctrl, state) = MakePanel();

        ctrl.SetSortMode(state, SortMode.Extension, 10);

        // Dirs (no ext): Assets, Docs (alphabetical by name within same empty ext)
        Assert.Equal("Assets", state.Items[0].Name);
        Assert.Equal("Docs", state.Items[1].Name);
        // Files: .csv < .txt; within .txt: a_file < c_file
        Assert.Equal("b_file.csv", state.Items[2].Name);
        Assert.Equal("a_file.txt", state.Items[3].Name);
        Assert.Equal("c_file.txt", state.Items[4].Name);
    }

    [Fact]
    public void SetSortMode_SortsBySize()
    {
        var (ctrl, state) = MakePanel();

        ctrl.SetSortMode(state, SortMode.Size, 10);

        // Files by size asc: a(50), c(100), b(200)
        Assert.Equal("a_file.txt", state.Items[2].Name);
        Assert.Equal("c_file.txt", state.Items[3].Name);
        Assert.Equal("b_file.csv", state.Items[4].Name);
    }

    [Fact]
    public void SetSortMode_SortsByLastWriteTime()
    {
        var (ctrl, state) = MakePanel();

        ctrl.SetSortMode(state, SortMode.LastWriteTime, 10);

        // Dirs by date asc: Docs(Jan 1), Assets(Mar 1)
        Assert.Equal("Docs", state.Items[0].Name);
        Assert.Equal("Assets", state.Items[1].Name);
        // Files by date asc: a(Jan 15), c(Feb 1), b(Apr 1)
        Assert.Equal("a_file.txt", state.Items[2].Name);
        Assert.Equal("c_file.txt", state.Items[3].Name);
        Assert.Equal("b_file.csv", state.Items[4].Name);
    }

    [Fact]
    public void SetSortMode_TogglesDescendingOnSameMode()
    {
        var (ctrl, state) = MakePanel();

        ctrl.SetSortMode(state, SortMode.Name, 10); // same mode → descending

        // Dirs desc: Docs, Assets
        Assert.Equal("Docs", state.Items[0].Name);
        Assert.Equal("Assets", state.Items[1].Name);
        // Files desc: c_file.txt, b_file.csv, a_file.txt
        Assert.Equal("c_file.txt", state.Items[2].Name);
        Assert.Equal("b_file.csv", state.Items[3].Name);
        Assert.Equal("a_file.txt", state.Items[4].Name);
    }

    [Fact]
    public void SetSortMode_DirectoriesAlwaysBeforeFiles()
    {
        var (ctrl, state) = MakePanel();

        foreach (SortMode mode in Enum.GetValues<SortMode>())
        {
            ctrl.SetSortMode(state, mode, 10);
            Assert.True(state.Items[0].IsDirectory, $"First item should be a dir in mode {mode}");
            Assert.True(state.Items[1].IsDirectory, $"Second item should be a dir in mode {mode}");
            Assert.False(state.Items[2].IsDirectory, $"Third item should be a file in mode {mode}");
        }
    }

    [Fact]
    public void SetSortMode_PreservesCursorByName()
    {
        var (ctrl, state) = MakePanel();
        state.CursorIndex = 4; // c_file.txt

        ctrl.SetSortMode(state, SortMode.Size, 10);
        // After size sort: [Assets, Docs, a_file(50), c_file(100), b_file(200)]
        // c_file.txt is now at index 3

        Assert.Equal("c_file.txt", ctrl.CurrentItem(state)?.Name);
    }
}
