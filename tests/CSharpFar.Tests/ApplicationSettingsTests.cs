using System.Reflection;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarAppSettingsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_AppliesDefaultSortModeFromSettings()
    {
        var app = CreateApp("Size",
            new FilePanelItem { Name = "large.txt", FullPath = Path.Combine(_tempDir, "large.txt"), IsDirectory = false, Size = 100 },
            new FilePanelItem { Name = "small.txt", FullPath = Path.Combine(_tempDir, "small.txt"), IsDirectory = false, Size = 1 });

        var left = GetLeftPanel(app);

        Assert.Equal(SortMode.Size, left.SortMode);
        Assert.Equal("small.txt", left.Items[0].Name);
        Assert.Equal("large.txt", left.Items[1].Name);
    }

    [Fact]
    public void Constructor_UsesNameSortWhenDefaultSortModeIsInvalid()
    {
        var app = CreateApp("not-a-sort-mode",
            new FilePanelItem { Name = "b.txt", FullPath = Path.Combine(_tempDir, "b.txt"), IsDirectory = false },
            new FilePanelItem { Name = "a.txt", FullPath = Path.Combine(_tempDir, "a.txt"), IsDirectory = false });

        var left = GetLeftPanel(app);

        Assert.Equal(SortMode.Name, left.SortMode);
        Assert.Equal("a.txt", left.Items[0].Name);
        Assert.Equal("b.txt", left.Items[1].Name);
    }

    private Application CreateApp(string sortMode, params FilePanelItem[] items)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir, items);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;
        settings.Panels.DefaultSortMode = sortMode;

        return new Application(
            new ScreenRenderer(new FakeConsoleDriver()),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings);
    }

    private static FilePanelState GetLeftPanel(Application app)
    {
        var field = typeof(Application).GetField("_left", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Application._left field not found.");
        return (FilePanelState)field.GetValue(app)!;
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
    }

    private sealed class NoOpFileOperationService : IFileOperationService
    {
        public Task CopyAsync(
            IReadOnlyList<string> sources,
            string destination,
            Action<string>? onProgress = null,
            Func<string, ConflictChoice>? onConflict = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MoveAsync(
            IReadOnlyList<string> sources,
            string destination,
            Func<string, ConflictChoice>? onConflict = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void CreateDirectory(string path) { }
    }
}
