using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class PanelControllerTests
{
    private const string Root = @"C:\Root";
    private const string Sub1 = @"C:\Root\Sub1";

    private static (PanelController ctrl, FilePanelState state) MakePanel(int itemCount = 10)
    {
        var fs = new FakeFileSystemService();
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new FilePanelItem
            {
                Name = $"file{i:D2}.txt",
                FullPath = $@"{Root}\file{i:D2}.txt",
                IsDirectory = false,
                Size = i * 100,
            })
            .ToArray();

        fs.AddDirectory(Root, items);
        var ctrl  = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);
        return (ctrl, state);
    }

    // ── LoadDirectory ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadDirectory_PopulatesItems()
    {
        var (ctrl, state) = MakePanel(5);
        Assert.Equal(5, state.Items.Count);
        Assert.Equal(Root, state.CurrentDirectory);
    }

    [Fact]
    public void LoadDirectory_ResetsCursorAndScroll()
    {
        var (ctrl, state) = MakePanel(10);
        state.CursorIndex  = 5;
        state.ScrollOffset = 3;

        ctrl.LoadDirectory(state, Root);

        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(0, state.ScrollOffset);
    }

    [Fact]
    public void LoadDirectory_ClearsSelection()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "file.txt", FullPath = Root + @"\file.txt", IsDirectory = false });
        fs.AddDirectory(Sub1,
            new FilePanelItem { Name = "other.txt", FullPath = Sub1 + @"\other.txt", IsDirectory = false });

        var ctrl = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);
        state.SelectedPaths.Add(Root + @"\file.txt");

        ctrl.LoadDirectory(state, Sub1);

        Assert.Empty(state.SelectedPaths);
    }

    [Fact]
    public void RefreshDirectory_PreservesExistingSelection()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "a.txt", FullPath = Root + @"\a.txt", IsDirectory = false },
            new FilePanelItem { Name = "b.txt", FullPath = Root + @"\b.txt", IsDirectory = false });

        var ctrl = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);
        state.SelectedPaths.Add(Root + @"\a.txt");
        state.SelectedPaths.Add(Root + @"\b.txt");

        fs.AddDirectory(Root,
            new FilePanelItem { Name = "a.txt", FullPath = Root + @"\a.txt", IsDirectory = false },
            new FilePanelItem { Name = "c.txt", FullPath = Root + @"\c.txt", IsDirectory = false });

        ctrl.RefreshDirectory(state, visibleRows: 10);

        Assert.Contains(Root + @"\a.txt", state.SelectedPaths);
        Assert.DoesNotContain(Root + @"\b.txt", state.SelectedPaths);
        Assert.Single(state.SelectedPaths);
    }

    // ── MoveCursor ────────────────────────────────────────────────────────────

    [Fact]
    public void MoveCursor_Down_IncrementsCursor()
    {
        var (ctrl, state) = MakePanel(10);
        ctrl.MoveCursor(state, +1, 10);
        Assert.Equal(1, state.CursorIndex);
    }

    [Fact]
    public void MoveCursor_DoesNotGoAboveZero()
    {
        var (ctrl, state) = MakePanel(10);
        ctrl.MoveCursor(state, -5, 10);
        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void MoveCursor_DoesNotGoBeyondLastItem()
    {
        var (ctrl, state) = MakePanel(5);
        ctrl.MoveCursor(state, +100, 10);
        Assert.Equal(4, state.CursorIndex);
    }

    [Fact]
    public void MoveCursor_AdjustsScrollDownWhenCursorExitsView()
    {
        var (ctrl, state) = MakePanel(20);
        ctrl.MoveCursor(state, +10, 5); // visible=5, cursor=10 → scrollOffset should be 6

        Assert.Equal(10, state.CursorIndex);
        Assert.True(state.ScrollOffset <= state.CursorIndex);
        Assert.True(state.CursorIndex < state.ScrollOffset + 5);
    }

    [Fact]
    public void MoveCursor_AdjustsScrollUpWhenCursorExitsView()
    {
        var (ctrl, state) = MakePanel(20);
        // Move down first
        state.CursorIndex  = 15;
        state.ScrollOffset = 12;

        ctrl.MoveCursor(state, -10, 5); // cursor = 5

        Assert.Equal(5, state.CursorIndex);
        Assert.Equal(5, state.ScrollOffset);
    }

    // ── MoveToFirst / MoveToLast ──────────────────────────────────────────────

    [Fact]
    public void MoveToFirst_SetsCursorToZero()
    {
        var (ctrl, state) = MakePanel(10);
        state.CursorIndex  = 7;
        state.ScrollOffset = 5;

        ctrl.MoveToFirst(state);

        Assert.Equal(0, state.CursorIndex);
        Assert.Equal(0, state.ScrollOffset);
    }

    [Fact]
    public void MoveToLast_SetsCursorToLastItem()
    {
        var (ctrl, state) = MakePanel(10);
        ctrl.MoveToLast(state, 5);
        Assert.Equal(9, state.CursorIndex);
    }

    // ── Page navigation ───────────────────────────────────────────────────────

    [Fact]
    public void MoveCursor_PageDown_MovesFullPage()
    {
        var (ctrl, state) = MakePanel(20);
        ctrl.MoveCursor(state, +5, 5); // page size = 5
        Assert.Equal(5, state.CursorIndex);
    }

    [Fact]
    public void MoveCursor_PageUp_MovesFullPage()
    {
        var (ctrl, state) = MakePanel(20);
        state.CursorIndex  = 10;
        state.ScrollOffset = 6;

        ctrl.MoveCursor(state, -5, 5);

        Assert.Equal(5, state.CursorIndex);
    }

    // ── CurrentItem ───────────────────────────────────────────────────────────

    [Fact]
    public void CurrentItem_ReturnsItemUnderCursor()
    {
        var (ctrl, state) = MakePanel(5);
        state.CursorIndex = 2;

        var item = ctrl.CurrentItem(state);

        Assert.NotNull(item);
        Assert.Equal("file02.txt", item.Name);
    }

    [Fact]
    public void CurrentItem_ReturnsNullForEmptyList()
    {
        var fs    = new FakeFileSystemService();
        fs.AddDirectory(Root);
        var ctrl  = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);

        Assert.Null(ctrl.CurrentItem(state));
    }

    // ── GoToParent ────────────────────────────────────────────────────────────

    [Fact]
    public void GoToParent_NavigatesToParentDirectory()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Sub1);
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "Sub1", FullPath = Sub1, IsDirectory = true });

        var ctrl  = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Sub1 };
        ctrl.LoadDirectory(state, Sub1);

        ctrl.GoToParent(state, visibleRows: 10);

        Assert.Equal(Root, state.CurrentDirectory);
    }

    [Fact]
    public void GoToParent_PositionsCursorOnChildDirectory()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Sub1);
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "OtherDir", FullPath = @"C:\Root\OtherDir", IsDirectory = true },
            new FilePanelItem { Name = "Sub1",     FullPath = Sub1,                IsDirectory = true },
            new FilePanelItem { Name = "ZDir",     FullPath = @"C:\Root\ZDir",     IsDirectory = true });

        var ctrl  = new PanelController(fs);
        var state = new FilePanelState { CurrentDirectory = Sub1 };
        ctrl.LoadDirectory(state, Sub1);

        ctrl.GoToParent(state, visibleRows: 10);

        Assert.Equal("Sub1", ctrl.CurrentItem(state)?.Name);
    }
}
