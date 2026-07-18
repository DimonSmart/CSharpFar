using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Mount points: PanelViewBuilder detects mount points via IVolumeMountPointService.
/// Uses a fake service to avoid Windows-specific P/Invoke in tests.
/// </summary>
public sealed class Spec007MountPointTests
{
    private const string Dir = @"C:\Root";

    private sealed class FakeVolumeMountPointService : IVolumeMountPointService
    {
        private readonly HashSet<string> _mountPaths =
            new(StringComparer.OrdinalIgnoreCase);
        public int CallCount { get; private set; }

        public void RegisterMountPoint(string path) => _mountPaths.Add(path);

        public VolumeMountPointInfo GetMountPointInfo(string directoryPath)
        {
            CallCount++;
            if (_mountPaths.Contains(directoryPath))
                return new VolumeMountPointInfo
                {
                    IsVolumeMountPoint = true,
                    VolumeName = @"\\?\Volume{fake}",
                    VolumePath = directoryPath,
                };
            return new VolumeMountPointInfo { IsVolumeMountPoint = false };
        }
    }

    private static PanelViewRequest Request(string path, bool detectMount) =>
        new()
        {
            DirectoryPath = path,
            Options = new AppSettings.PanelOptionsSettings
            {
                DetectVolumeMountPoints = detectMount,
            },
            SortMode = SortMode.Name,
            SortDescending = false,
            SelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    // ── Detection enabled ─────────────────────────────────────────────────────

    [Fact]
    public void DetectEnabled_MountPoint_IsFilled()
    {
        var mountPath = Dir + @"\Data";
        var fakeSvc = new FakeVolumeMountPointService();
        fakeSvc.RegisterMountPoint(mountPath);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "Data", FullPath = mountPath, IsDirectory = true });

        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: fakeSvc);
        var view = builder.Build(Request(Dir, detectMount: true));

        var item = view.Items.First(i => i.Name == "Data");
        Assert.True(item.IsVolumeMountPoint);
        Assert.Equal(@"\\?\Volume{fake}", item.MountedVolumeName);
    }

    [Fact]
    public void DetectEnabled_NonMountPoint_IsNotMarked()
    {
        var dirPath = Dir + @"\Docs";
        var fakeSvc = new FakeVolumeMountPointService(); // no mount points registered

        var fs = new FakeFileSystemService();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "Docs", FullPath = dirPath, IsDirectory = true });

        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: fakeSvc);
        var view = builder.Build(Request(Dir, detectMount: true));

        var item = view.Items.First(i => i.Name == "Docs");
        Assert.False(item.IsVolumeMountPoint);
        Assert.Null(item.MountedVolumeName);
    }

    // ── Detection disabled ────────────────────────────────────────────────────

    [Fact]
    public void DetectDisabled_ServiceNotCalled()
    {
        var mountPath = Dir + @"\Data";
        var fakeSvc = new FakeVolumeMountPointService();
        fakeSvc.RegisterMountPoint(mountPath);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "Data", FullPath = mountPath, IsDirectory = true });

        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: fakeSvc);
        var view = builder.Build(Request(Dir, detectMount: false));

        Assert.Equal(0, fakeSvc.CallCount);

        var item = view.Items.First(i => i.Name == "Data");
        Assert.False(item.IsVolumeMountPoint);
    }

    // ── Mount point counts as directory in summary ────────────────────────────

    [Fact]
    public void MountPoint_CountedAsDirectory_InSummary()
    {
        var mountPath = Dir + @"\Storage";
        var fakeSvc = new FakeVolumeMountPointService();
        fakeSvc.RegisterMountPoint(mountPath);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "Storage", FullPath = mountPath, IsDirectory = true });

        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: fakeSvc);
        var view = builder.Build(Request(Dir, detectMount: true));

        Assert.Equal(1, view.Summary.DirectoryCount);
        Assert.Equal(0, view.Summary.FileCount);
    }

    // ── No service, detection enabled → items keep defaults ──────────────────

    [Fact]
    public void NoMountPointService_DetectEnabled_ItemsNotMarked()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "Dir", FullPath = Dir + @"\Dir", IsDirectory = true });

        // null mount point service
        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: null);
        var view = builder.Build(Request(Dir, detectMount: true));

        var item = view.Items.First(i => i.Name == "Dir");
        Assert.False(item.IsVolumeMountPoint);
    }

    [Fact]
    public void CurrentDirectoryMountPoint_IsTreatedAsRoot_ForParentItem()
    {
        var mountRoot = Dir + @"\Mounted";
        var fakeSvc = new FakeVolumeMountPointService();
        fakeSvc.RegisterMountPoint(mountRoot);

        var fs = new FakeFileSystemService();
        fs.AddDirectory(mountRoot);

        var builder = new PanelViewBuilder(fs, new PanelSortService(), mountPoints: fakeSvc);
        var view = builder.Build(new PanelViewRequest
        {
            DirectoryPath = mountRoot,
            Options = new AppSettings.PanelOptionsSettings
            {
                DetectVolumeMountPoints = true,
                ShowParentDirectoryInRootFolders = false,
            },
            SortMode = SortMode.Name,
            SortDescending = false,
            SelectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        });

        Assert.True(view.IsRootDirectory);
        Assert.DoesNotContain(view.Items, i => i.IsParentDirectory);
    }
}
