using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 6: F7 create folder — FileOperationService and SetCursorByName.
/// </summary>
public class CreateFolderTests
{
    // ── FileOperationService ──────────────────────────────────────────────────

    [Fact]
    public void CreateDirectory_CreatesOnDisk()
    {
        var svc  = new FileOperationService();
        string p = TempPath();
        try
        {
            svc.CreateDirectory(p);
            Assert.True(Directory.Exists(p));
        }
        finally { if (Directory.Exists(p)) Directory.Delete(p); }
    }

    [Fact]
    public void CreateDirectory_ThrowsIOExceptionIfAlreadyExists()
    {
        var svc  = new FileOperationService();
        string p = TempPath();
        Directory.CreateDirectory(p);
        try
        {
            Assert.Throws<IOException>(() => svc.CreateDirectory(p));
        }
        finally { Directory.Delete(p); }
    }

    [Fact]
    public void CreateDirectory_ThrowsOnInvalidPath()
    {
        var svc = new FileOperationService();
        // A path with a null character is invalid on Windows
        Assert.ThrowsAny<Exception>(() => svc.CreateDirectory("C:\\nul\0bad"));
    }

    // ── PanelController.SetCursorByName ──────────────────────────────────────

    [Fact]
    public void SetCursorByName_PositionsCursorOnNamedItem()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(@"C:\Root",
            new FilePanelItem { Name = "alpha", FullPath = @"C:\Root\alpha", IsDirectory = true  },
            new FilePanelItem { Name = "beta",  FullPath = @"C:\Root\beta",  IsDirectory = false });

        var ctrl  = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        ctrl.LoadDirectory(state, @"C:\Root");

        ctrl.SetCursorByName(state, "beta", 10);

        Assert.Equal("beta", ctrl.CurrentItem(state)?.Name);
    }

    [Fact]
    public void SetCursorByName_IsCaseInsensitive()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(@"C:\Root",
            new FilePanelItem { Name = "NewFolder", FullPath = @"C:\Root\NewFolder", IsDirectory = true });

        var ctrl  = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        ctrl.LoadDirectory(state, @"C:\Root");

        ctrl.SetCursorByName(state, "newfolder", 10);

        Assert.Equal("NewFolder", ctrl.CurrentItem(state)?.Name);
    }

    [Fact]
    public void SetCursorByName_DoesNothingWhenNotFound()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(@"C:\Root",
            new FilePanelItem { Name = "file.txt", FullPath = @"C:\Root\file.txt", IsDirectory = false });

        var ctrl  = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        ctrl.LoadDirectory(state, @"C:\Root");
        state.CursorIndex = 0;

        ctrl.SetCursorByName(state, "NonExistent", 10);

        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void SetCursorByName_EnsuresItemVisible()
    {
        var fs = new FakeFileSystemService();
        var items = Enumerable.Range(0, 20)
            .Select(i => new FilePanelItem
            {
                Name = $"item{i:D2}", FullPath = $@"C:\Root\item{i:D2}", IsDirectory = false,
            })
            .ToArray();
        fs.AddDirectory(@"C:\Root", items);

        var ctrl  = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"C:\Root" };
        ctrl.LoadDirectory(state, @"C:\Root");

        ctrl.SetCursorByName(state, "item15", visibleRows: 5);

        Assert.Equal("item15", ctrl.CurrentItem(state)?.Name);
        Assert.True(state.CursorIndex >= state.ScrollOffset);
        Assert.True(state.CursorIndex < state.ScrollOffset + 5);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"CSharpFarTest_{Guid.NewGuid():N}");
}
