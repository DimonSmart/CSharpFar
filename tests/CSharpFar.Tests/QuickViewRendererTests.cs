using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 16: QuickViewRenderer draws the correct content for
/// null items, directories, and text files.
/// </summary>
public class QuickViewRendererTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeConsoleDriver _driver;
    private readonly ScreenRenderer _screen;
    private readonly QuickViewRenderer _renderer;
    private readonly Rect _bounds = new(0, 0, 40, 10);

    public QuickViewRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarQVTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _driver = new FakeConsoleDriver(80, 25);
        _screen = new ScreenRenderer(_driver);
        _renderer = new QuickViewRenderer(_screen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string ContentRow(int row) =>
        _driver.GetRegionText(new Rect(_bounds.X + 1, _bounds.Y + 1 + row, _bounds.Width - 2, 1));

    [Theory]
    [MemberData(nameof(NoSelectionItems))]
    public void NoPreviewableItem_ShowsNoFileSelected(FilePanelItem? item)
    {
        _renderer.Render(_bounds, item);

        Assert.Contains("No file selected", ContentRow(0));
    }

    [Fact]
    public void DirectoryItem_ShowsPathAndDirectCounts()
    {
        string subDir = Path.Combine(_tempDir, "testDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "world");

        var item = new FilePanelItem { Name = "testDir", FullPath = subDir, IsDirectory = true };
        _renderer.Render(_bounds, item);

        string row0 = ContentRow(0);
        Assert.Contains("testDir", row0);
        Assert.Contains("Files:", ContentRow(6));
        Assert.Contains("2", ContentRow(6));
        Assert.Contains("Directories:", ContentRow(7));
        Assert.Contains("0", ContentRow(7));
    }

    [Fact]
    public void FileItem_ShowsTextContent()
    {
        string filePath = Path.Combine(_tempDir, "preview.txt");
        File.WriteAllText(filePath, "line one\nline two\nline three");

        var item = new FilePanelItem { Name = "preview.txt", FullPath = filePath, IsDirectory = false };
        _renderer.Render(_bounds, item);

        Assert.Contains("line one", ContentRow(0));
        Assert.Contains("line two", ContentRow(1));
        Assert.Contains("line three", ContentRow(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Render_NarrowBounds_DoesNotThrow(int width)
    {
        string filePath = Path.Combine(_tempDir, "narrow.txt");
        File.WriteAllText(filePath, "line one");

        var directoryItem = new FilePanelItem { Name = "dir", FullPath = _tempDir, IsDirectory = true };
        var fileItem = new FilePanelItem { Name = "narrow.txt", FullPath = filePath, IsDirectory = false };
        var bounds = new Rect(0, 0, width, 5);

        _renderer.Render(bounds, null);
        _renderer.Render(bounds, directoryItem);
        _renderer.Render(bounds, fileItem);
    }

    public static TheoryData<FilePanelItem?> NoSelectionItems() => new()
    {
        null,
        new FilePanelItem { Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true },
    };
}
