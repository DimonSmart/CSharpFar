using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Visibility: showHiddenAndSystemFiles filtering in PanelViewBuilder.
/// </summary>
public sealed class Spec007VisibilityTests
{
    private const string Dir = @"C:\TestDir";

    private static (PanelViewBuilder builder, FakeFileSystemService fs) MakeBuilder()
    {
        var fs      = new FakeFileSystemService();
        var sort    = new PanelSortService();
        var builder = new PanelViewBuilder(fs, sort);
        return (builder, fs);
    }

    private static PanelViewRequest MakeRequest(string path, bool showHidden) =>
        new()
        {
            DirectoryPath  = path,
            Options        = new AppSettings.PanelOptionsSettings { ShowHiddenAndSystemFiles = showHidden },
            SortMode       = SortMode.Name,
            SortDescending = false,
            SelectedPaths  = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public void HiddenFile_Visible_WhenShowHidden_True()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "hidden.txt", FullPath = Dir + @"\hidden.txt", IsDirectory = false,
                                Attributes = FileAttributes.Hidden });

        var view = builder.Build(MakeRequest(Dir, showHidden: true));

        Assert.Contains(view.Items, i => i.Name == "hidden.txt");
    }

    [Fact]
    public void HiddenFile_Hidden_WhenShowHidden_False()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "hidden.txt", FullPath = Dir + @"\hidden.txt", IsDirectory = false,
                                Attributes = FileAttributes.Hidden });

        var view = builder.Build(MakeRequest(Dir, showHidden: false));

        Assert.DoesNotContain(view.Items, i => i.Name == "hidden.txt");
    }

    [Fact]
    public void SystemDirectory_Hidden_WhenShowHidden_False()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "sys", FullPath = Dir + @"\sys", IsDirectory = true,
                                Attributes = FileAttributes.Directory | FileAttributes.System });

        var view = builder.Build(MakeRequest(Dir, showHidden: false));

        Assert.DoesNotContain(view.Items, i => i.Name == "sys");
    }

    [Fact]
    public void NormalFile_NotFiltered_WhenShowHidden_False()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "normal.txt", FullPath = Dir + @"\normal.txt", IsDirectory = false,
                                Attributes = FileAttributes.Normal });

        var view = builder.Build(MakeRequest(Dir, showHidden: false));

        Assert.Contains(view.Items, i => i.Name == "normal.txt");
    }

    [Fact]
    public void ParentDotDot_NotFiltered_EvenWhenShowHidden_False()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir); // empty, so builder adds ..

        var view = builder.Build(MakeRequest(Dir, showHidden: false));

        // PanelViewBuilder adds ".." for non-root paths; Dir is not root
        Assert.Contains(view.Items, i => i.IsParentDirectory);
    }

    [Fact]
    public void Summary_ExcludesFilteredHiddenFiles()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "visible.txt", FullPath = Dir + @"\visible.txt",
                                IsDirectory = false, Attributes = FileAttributes.Normal, Size = 100 },
            new FilePanelItem { Name = "hidden.txt",  FullPath = Dir + @"\hidden.txt",
                                IsDirectory = false, Attributes = FileAttributes.Hidden, Size = 500 });

        var view = builder.Build(MakeRequest(Dir, showHidden: false));

        // Only visible.txt should count; hidden.txt is filtered
        Assert.Equal(1, view.Summary.FileCount);
        Assert.Equal(100, view.Summary.TotalFileSize);
    }

    [Fact]
    public void Summary_IncludesHiddenFiles_WhenShowHidden_True()
    {
        var (builder, fs) = MakeBuilder();
        fs.AddDirectory(Dir,
            new FilePanelItem { Name = "visible.txt", FullPath = Dir + @"\visible.txt",
                                IsDirectory = false, Attributes = FileAttributes.Normal, Size = 100 },
            new FilePanelItem { Name = "hidden.txt",  FullPath = Dir + @"\hidden.txt",
                                IsDirectory = false, Attributes = FileAttributes.Hidden, Size = 500 });

        var view = builder.Build(MakeRequest(Dir, showHidden: true));

        Assert.Equal(2, view.Summary.FileCount);
        Assert.Equal(600, view.Summary.TotalFileSize);
    }
}
