using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Selection: selectFolders option and CanSelect predicate.
/// </summary>
public sealed class Spec007SelectionTests
{
    private const string Root = @"C:\Root";

    private static (PanelController ctrl, FilePanelState state) MakePanel(
        AppSettings.PanelOptionsSettings? opts = null)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "SubDir", FullPath = Root + @"\SubDir", IsDirectory = true },
            new FilePanelItem { Name = "file.txt", FullPath = Root + @"\file.txt", IsDirectory = false });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root, opts);
        return (ctrl, state);
    }

    // ── CanSelect ─────────────────────────────────────────────────────────────

    [Fact]
    public void CanSelect_ParentDirectory_ReturnsFalse()
    {
        var item = new FilePanelItem
        {
            Name = "..",
            IsDirectory = true,
            IsParentDirectory = true,
            FullPath = @"C:\"
        };
        var opts = new AppSettings.PanelOptionsSettings();

        Assert.False(PanelController.CanSelect(item, opts));
    }

    [Fact]
    public void CanSelect_Directory_ReturnsTrue_WhenSelectFolders_True()
    {
        var item = new FilePanelItem { Name = "Dir", IsDirectory = true, FullPath = Root + @"\Dir" };
        var opts = new AppSettings.PanelOptionsSettings { SelectFolders = true };

        Assert.True(PanelController.CanSelect(item, opts));
    }

    [Fact]
    public void CanSelect_Directory_ReturnsFalse_WhenSelectFolders_False()
    {
        var item = new FilePanelItem { Name = "Dir", IsDirectory = true, FullPath = Root + @"\Dir" };
        var opts = new AppSettings.PanelOptionsSettings { SelectFolders = false };

        Assert.False(PanelController.CanSelect(item, opts));
    }

    [Fact]
    public void CanSelect_File_AlwaysTrue_Regardless_SelectFolders()
    {
        var item = new FilePanelItem { Name = "f.txt", IsDirectory = false, FullPath = Root + @"\f.txt" };

        Assert.True(PanelController.CanSelect(item,
            new AppSettings.PanelOptionsSettings { SelectFolders = false }));
        Assert.True(PanelController.CanSelect(item,
            new AppSettings.PanelOptionsSettings { SelectFolders = true }));
    }

    // ── ToggleSelectAll ───────────────────────────────────────────────────────

    [Fact]
    public void ToggleSelectAll_SelectsFolders_WhenSelectFolders_True()
    {
        var (ctrl, state) = MakePanel();

        ctrl.ToggleSelectAll(state, new AppSettings.PanelOptionsSettings { SelectFolders = true });

        Assert.Contains(Root + @"\SubDir", state.SelectedPaths);
        Assert.Contains(Root + @"\file.txt", state.SelectedPaths);
    }

    [Fact]
    public void ToggleSelectAll_SkipsFolders_WhenSelectFolders_False()
    {
        var (ctrl, state) = MakePanel();
        var opts = new AppSettings.PanelOptionsSettings { SelectFolders = false };

        ctrl.ToggleSelectAll(state, opts);

        Assert.DoesNotContain(Root + @"\SubDir", state.SelectedPaths);
        Assert.Contains(Root + @"\file.txt", state.SelectedPaths);
    }

    // ── InvertSelection ───────────────────────────────────────────────────────

    [Fact]
    public void InvertSelection_SkipsFolders_WhenSelectFolders_False()
    {
        var (ctrl, state) = MakePanel();
        var opts = new AppSettings.PanelOptionsSettings { SelectFolders = false };

        ctrl.InvertSelection(state, opts);

        Assert.DoesNotContain(Root + @"\SubDir", state.SelectedPaths);
        Assert.Contains(Root + @"\file.txt", state.SelectedPaths);
    }

    [Fact]
    public void InvertSelection_IncludesFolders_WhenSelectFolders_True()
    {
        var (ctrl, state) = MakePanel();

        ctrl.InvertSelection(state, new AppSettings.PanelOptionsSettings { SelectFolders = true });

        Assert.Contains(Root + @"\SubDir", state.SelectedPaths);
        Assert.Contains(Root + @"\file.txt", state.SelectedPaths);
    }

    [Fact]
    public void ToggleCurrentSelection_DoesNotMoveCursor_AndUpdatesSummary()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "a.txt", FullPath = Root + @"\a.txt", IsDirectory = false, Size = 10 },
            new FilePanelItem { Name = "b.txt", FullPath = Root + @"\b.txt", IsDirectory = false, Size = 20 });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);
        state.CursorIndex = 1;

        ctrl.ToggleCurrentSelection(state);

        Assert.Equal(1, state.CursorIndex);
        Assert.Contains(Root + @"\b.txt", state.SelectedPaths);
        Assert.Equal(1, state.Summary?.SelectedCount);
        Assert.Equal(20, state.Summary?.SelectedFileSize);
    }

    [Fact]
    public void ToggleSelectAll_UpdatesSummary()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Root,
            new FilePanelItem { Name = "a.txt", FullPath = Root + @"\a.txt", IsDirectory = false, Size = 10 },
            new FilePanelItem { Name = "dir", FullPath = Root + @"\dir", IsDirectory = true });

        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = Root };
        ctrl.LoadDirectory(state, Root);

        ctrl.ToggleSelectAll(state, new AppSettings.PanelOptionsSettings { SelectFolders = true });

        Assert.Equal(2, state.Summary?.SelectedCount);
        Assert.Equal(10, state.Summary?.SelectedFileSize);
    }
}
