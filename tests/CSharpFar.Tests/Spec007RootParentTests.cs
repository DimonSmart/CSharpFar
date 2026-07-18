using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Root parent: showParentDirectoryInRootFolders and root detection.
/// </summary>
public sealed class Spec007RootParentTests
{
    private static PanelViewBuilder MakeWindowsBuilder(IFileSystemService fs)
    {
        var sort = new PanelSortService();
        return new PanelViewBuilder(fs, sort, pathSemantics: new WindowsPanelPathSemantics());
    }

    private static PanelViewBuilder MakeUnixBuilder(IFileSystemService fs)
    {
        var sort = new PanelSortService();
        return new PanelViewBuilder(fs, sort, pathSemantics: new UnixPanelPathSemantics());
    }

    private static PanelViewRequest Request(string path, bool showParentInRoot) =>
        new()
        {
            DirectoryPath = path,
            Options = new AppSettings.PanelOptionsSettings
            {
                ShowParentDirectoryInRootFolders = showParentInRoot,
            },
            SortMode = SortMode.Name,
            SortDescending = false,
            SelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    // ── Non-root always shows ".." ─────────────────────────────────────────────

    [Fact]
    public void WindowsNonRoot_ShowsParent_Regardless_OfOption()
    {
        var path = @"C:\Users\Test";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var viewFalse = builder.Build(Request(path, showParentInRoot: false));
        var viewTrue = builder.Build(Request(path, showParentInRoot: true));

        Assert.Contains(viewFalse.Items, i => i.IsParentDirectory);
        Assert.Contains(viewTrue.Items, i => i.IsParentDirectory);
    }

    // ── Windows drive root ─────────────────────────────────────────────────────

    [Fact]
    public void WindowsDriveRoot_HidesParent_WhenShowParentInRoot_False()
    {
        var path = @"C:\";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.DoesNotContain(view.Items, i => i.IsParentDirectory);
    }

    [Fact]
    public void WindowsDriveRoot_ShowsParent_WhenShowParentInRoot_True()
    {
        var path = @"C:\";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: true));

        Assert.Contains(view.Items, i => i.IsParentDirectory);
    }

    [Fact]
    public void WindowsDriveRoot_IsRootDirectory_True()
    {
        var path = @"C:\";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.True(view.IsRootDirectory);
    }

    [Fact]
    public void WindowsNonRoot_IsRootDirectory_False()
    {
        var path = @"C:\Windows";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.False(view.IsRootDirectory);
    }

    // ── UNC share root ────────────────────────────────────────────────────────

    [Fact]
    public void WindowsUncShareRoot_HidesParent_WhenShowParentInRoot_False()
    {
        var path = @"\\server\share";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.DoesNotContain(view.Items, i => i.IsParentDirectory);
    }

    [Fact]
    public void WindowsUncShareRoot_ShowsParent_WhenShowParentInRoot_True()
    {
        var path = @"\\server\share";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: true));

        Assert.Contains(view.Items, i => i.IsParentDirectory);
    }

    // ── Root ".." FullPath is same as current directory ────────────────────────

    [Fact]
    public void WindowsDriveRoot_ParentItem_FullPath_EqualsCurrentDirectory()
    {
        var path = @"C:\";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeWindowsBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: true));

        var dotdot = view.Items.First(i => i.IsParentDirectory);
        Assert.Equal(path, dotdot.FullPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnixRoot_HidesParent_WhenShowParentInRoot_False()
    {
        var path = "/";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeUnixBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.DoesNotContain(view.Items, i => i.IsParentDirectory);
    }

    [Fact]
    public void UnixRoot_ShowsParent_WhenShowParentInRoot_True()
    {
        var path = "/";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeUnixBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: true));

        var dotdot = Assert.Single(view.Items, i => i.IsParentDirectory);
        Assert.Equal(path, dotdot.FullPath);
    }

    [Fact]
    public void UnixNonRoot_ShowsParent_Regardless_OfOption()
    {
        var path = "/home/user";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeUnixBuilder(fs);

        var viewFalse = builder.Build(Request(path, showParentInRoot: false));
        var viewTrue = builder.Build(Request(path, showParentInRoot: true));

        Assert.Contains(viewFalse.Items, i => i.IsParentDirectory && i.FullPath == "/home");
        Assert.Contains(viewTrue.Items, i => i.IsParentDirectory && i.FullPath == "/home");
    }

    [Fact]
    public void UnixRoot_IsRootDirectory_True()
    {
        var path = "/";
        var fs = new FakeFileSystemService();
        fs.AddDirectory(path);
        var builder = MakeUnixBuilder(fs);

        var view = builder.Build(Request(path, showParentInRoot: false));

        Assert.True(view.IsRootDirectory);
    }
}
