using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec030PanelSourceErrorRetryTests
{
    [Fact]
    public void TryLoadDirectory_WhenLocalReadFails_ShowsPanelLoadErrorAtTarget()
    {
        var fs = new ToggleFileSystemService();
        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"C:\Old" };
        state.Items.Add(new FilePanelItem
        {
            Name = "old.txt",
            FullPath = @"C:\Old\old.txt",
            IsDirectory = false,
        });

        fs.Error = new IOException("Drive is not ready.");

        bool loaded = ctrl.TryLoadDirectory(state, @"R:\", new AppSettings.PanelOptionsSettings());

        Assert.False(loaded);
        Assert.Equal(@"R:\", state.CurrentDirectory);
        Assert.Empty(state.Items);
        Assert.NotNull(state.LoadError);
        Assert.Equal(@"R:\", state.LoadError.RetryLocation.SourcePath);
        Assert.Contains("Drive is not ready", state.LoadError.Message, StringComparison.Ordinal);
        Assert.Equal(PanelProviderCapabilities.Refresh, state.ProviderCapabilities);
    }

    [Fact]
    public void TryRefreshDirectory_AfterLocalSourceBecomesAvailable_ClearsPanelLoadError()
    {
        var fs = new ToggleFileSystemService { Error = new IOException("Drive is not ready.") };
        var ctrl = new PanelController(new FakePanelViewBuilder(fs));
        var state = new FilePanelState { CurrentDirectory = @"R:\" };

        Assert.False(ctrl.TryRefreshDirectory(state, visibleRows: 10));
        Assert.NotNull(state.LoadError);

        fs.Error = null;
        fs.AddDirectory(
            @"R:\",
            new FilePanelItem
            {
                Name = "ready.txt",
                FullPath = @"R:\ready.txt",
                IsDirectory = false,
            });

        Assert.True(ctrl.TryRefreshDirectory(state, visibleRows: 10));
        Assert.Null(state.LoadError);
        Assert.Single(state.Items);
        Assert.Equal("ready.txt", state.Items[0].Name);
    }

    [Fact]
    public void TryLoadLocation_WhenProviderEnumerationFails_ShowsPanelLoadErrorForProviderPath()
    {
        var source = new TogglePanelSource(new PanelSourceId("sftp:test"))
        {
            Error = new IOException("SFTP connection lost."),
        };
        var registry = new FilePanelSourceRegistry([source]);
        var builder = new PanelViewBuilder(
            new FakeFileSystemService(),
            new PanelSortService(),
            sources: registry);
        var ctrl = new PanelController(builder);
        var state = new FilePanelState { CurrentDirectory = @"C:\" };
        var location = new PanelLocation(source.SourceId, "/remote");

        bool loaded = ctrl.TryLoadLocation(state, location, new AppSettings.PanelOptionsSettings());

        Assert.False(loaded);
        Assert.Equal(source.SourceId, state.SourceId);
        Assert.Equal("/remote", state.SourcePath);
        Assert.NotNull(state.LoadError);
        Assert.Equal(location, state.LoadError.RetryLocation);
        Assert.Contains("SFTP connection lost", state.LoadError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRefreshDirectory_AfterProviderRecovers_LoadsProviderItems()
    {
        var source = new TogglePanelSource(new PanelSourceId("sftp:test"))
        {
            Error = new IOException("SFTP connection lost."),
        };
        var registry = new FilePanelSourceRegistry([source]);
        var builder = new PanelViewBuilder(
            new FakeFileSystemService(),
            new PanelSortService(),
            sources: registry);
        var ctrl = new PanelController(builder);
        var state = new FilePanelState { CurrentDirectory = @"C:\" };
        state.CurrentLocation = new PanelLocation(source.SourceId, "/remote");

        Assert.False(ctrl.TryRefreshDirectory(state, visibleRows: 10));

        source.Error = null;
        source.Items =
        [
            new FilePanelItem
            {
                Name = "remote.txt",
                FullPath = "/remote/remote.txt",
                SourceId = source.SourceId,
                IsDirectory = false,
            },
        ];

        Assert.True(ctrl.TryRefreshDirectory(state, visibleRows: 10));
        Assert.Null(state.LoadError);
        Assert.Single(state.Items);
        Assert.Equal("remote.txt", state.Items[0].Name);
        Assert.Equal(source.Capabilities, state.ProviderCapabilities);
    }

    [Fact]
    public void PanelRenderer_LoadError_ShowsMessageAndRetryButton()
    {
        var driver = new FakeConsoleDriver(width: 60, height: 18);
        var screen = new ScreenRenderer(driver);
        var state = new FilePanelState
        {
            CurrentDirectory = @"R:\",
            LoadError = new PanelLoadError
            {
                Message = "Drive is not ready.",
                RetryLocation = PanelLocation.Local(@"R:\"),
            },
        };

        UiTestRender.Render(screen, canvas =>
            new PanelRenderer(canvas, PaletteRegistry.Default)
                .Render(new Rect(0, 0, 60, 16), state, isActive: true));

        string panelText = driver.GetRegionText(new Rect(0, 0, 60, 16));
        Assert.Contains("Cannot read panel source", panelText, StringComparison.Ordinal);
        Assert.Contains("Drive is not ready.", panelText, StringComparison.Ordinal);
        Assert.Contains("[ Retry ]", panelText, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelErrorRenderer_HitTestRetry_ReturnsTrueOnlyOnRetryButton()
    {
        var state = new FilePanelState
        {
            CurrentDirectory = @"R:\",
            LoadError = new PanelLoadError
            {
                Message = "Drive is not ready.",
                RetryLocation = PanelLocation.Local(@"R:\"),
            },
        };
        var bounds = new Rect(0, 0, 60, 16);

        bool foundButton = false;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            for (int x = bounds.X; x < bounds.Right; x++)
            {
                if (PanelErrorRenderer.HitTestRetry(
                        x,
                        y,
                        bounds,
                        state,
                        PanelViewMode.Full,
                        options: null))
                {
                    foundButton = true;
                    Assert.False(PanelErrorRenderer.HitTestRetry(
                        x,
                        Math.Max(bounds.Y, y - 1),
                        bounds,
                        state,
                        PanelViewMode.Full,
                        options: null));
                    break;
                }
            }

            if (foundButton)
                break;
        }

        Assert.True(foundButton);
    }

    private sealed class ToggleFileSystemService : IFileSystemService
    {
        private readonly Dictionary<string, IReadOnlyList<FilePanelItem>> _directories =
            new(StringComparer.OrdinalIgnoreCase);

        public Exception? Error { get; set; }

        public void AddDirectory(string path, params FilePanelItem[] items)
        {
            _directories[path] = items;
        }

        public IReadOnlyList<FilePanelItem> ReadDirectory(string path)
        {
            if (Error is not null)
                throw Error;

            return _directories.TryGetValue(path, out var items) ? items : [];
        }

        public bool DirectoryExists(string path) =>
            Error is null && _directories.ContainsKey(path);

        public bool FileExists(string path) => false;
    }

    private sealed class TogglePanelSource : IFilePanelSource
    {
        public TogglePanelSource(PanelSourceId sourceId)
        {
            SourceId = sourceId;
        }

        public PanelSourceId SourceId { get; }
        public string DisplayName => "Test SFTP";
        public PanelProviderCapabilities Capabilities =>
            PanelProviderCapabilities.Enumerate |
            PanelProviderCapabilities.Refresh |
            PanelProviderCapabilities.CopyFrom;

        public Exception? Error { get; set; }
        public IReadOnlyList<FilePanelItem> Items { get; set; } = [];

        public string NormalizePath(string sourcePath) => sourcePath;
        public bool IsRootPath(string sourcePath) => true;
        public string? GetParentPath(string sourcePath) => null;

        public IReadOnlyList<FilePanelItem> EnumerateDirectory(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (Error is not null)
                throw Error;

            return Items;
        }

        public FilePanelItem? GetItem(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenWriteAsync(
            string sourcePath,
            bool overwrite,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string sourcePath,
            bool recursive,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(
            string sourcePath,
            string newSourcePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
