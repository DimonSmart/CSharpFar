using System.Collections.Concurrent;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PanelDirectorySizeCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarDirSizeCommands_{Guid.NewGuid():N}");

    public PanelDirectorySizeCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void F3_DirectoryRequiresEnumerationNotOpenReadAndStartsBackgroundTraversal()
    {
        var source = new ControlledSource(PanelProviderCapabilities.Enumerate);
        using var services = Services(source);
        FilePanelState panel = PreparePanel(services, source, Dir(source, "/root/a"));
        source.Set("/root/a", File(source, "/root/a/file.bin", 12));

        Assert.True(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, services.CommandContext));
        ApplicationCommandResult result = services.CommandRegistry.Execute(FunctionKeyCommandIds.View, services.CommandContext);

        Assert.True(result.ShouldRender);
        Assert.True(source.WaitForEnumeration("/root/a"));
        Assert.Equal(1, source.CallCount("/root/a"));
        Assert.Null(panel.Items[0].Size);
    }

    [Fact]
    public void F3_FileStillRequiresOpenRead()
    {
        var enumerateOnly = new ControlledSource(PanelProviderCapabilities.Enumerate);
        using var services = Services(enumerateOnly);
        PreparePanel(services, enumerateOnly, File(enumerateOnly, "/root/file.bin", 10));
        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, services.CommandContext));

        var readable = new ControlledSource(PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.OpenRead);
        using var readableServices = Services(readable);
        PreparePanel(readableServices, readable, File(readable, "/root/file.bin", 10));
        Assert.True(readableServices.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, readableServices.CommandContext));
    }

    [Fact]
    public void F3_ParentLinkAndMountDirectoryAreUnavailable()
    {
        var source = new ControlledSource(PanelProviderCapabilities.Enumerate);
        using var services = Services(source);

        var parent = new FilePanelItem
        {
            Name = "..",
            FullPath = "/",
            SourceId = source.SourceId,
            IsDirectory = true,
            IsParentDirectory = true,
        };
        FilePanelState panel = PreparePanel(services, source, parent);
        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, services.CommandContext));

        panel.Items[0] = Dir(source, "/root/link", FileAttributes.ReparsePoint);
        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, services.CommandContext));

        panel.Items[0] = Mount(source, "/root/mount");
        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.View, services.CommandContext));
        Assert.Empty(source.Calls);
    }

    [Fact]
    public void RepeatedF3_DoesNotStartDuplicateScan()
    {
        var source = new ControlledSource(PanelProviderCapabilities.Enumerate) { BlockEnumeration = true };
        using var services = Services(source);
        PreparePanel(services, source, Dir(source, "/root/a"));

        services.CommandRegistry.Execute(FunctionKeyCommandIds.View, services.CommandContext);
        Assert.True(source.WaitForEnumeration("/root/a"));
        services.CommandRegistry.Execute(FunctionKeyCommandIds.View, services.CommandContext);
        Thread.Sleep(50);

        Assert.Equal(1, source.CallCount("/root/a"));
        source.ReleaseEnumeration.Set();
    }

    [Fact]
    public void ShiftF3_QueuesAllEligibleDirectoriesAndRepeatedBatchDoesNotDuplicate()
    {
        var source = new ControlledSource(PanelProviderCapabilities.Enumerate) { BlockFirstEnumeration = true };
        using var services = Services(source);
        PreparePanel(
            services,
            source,
            Dir(source, "/root/a"),
            Dir(source, "/root/b"),
            File(source, "/root/file.bin", 1),
            Dir(source, "/root/link", FileAttributes.ReparsePoint),
            Mount(source, "/root/mount"));

        Assert.True(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.CalculateDirectorySizes, services.CommandContext));
        services.CommandRegistry.Execute(FunctionKeyCommandIds.CalculateDirectorySizes, services.CommandContext);
        Assert.True(source.WaitForEnumeration("/root/a"));
        services.CommandRegistry.Execute(FunctionKeyCommandIds.CalculateDirectorySizes, services.CommandContext);
        Thread.Sleep(50);
        Assert.Equal(1, source.CallCount("/root/a"));
        Assert.Equal(0, source.CallCount("/root/b"));

        source.ReleaseEnumeration.Set();
        Assert.True(source.WaitForEnumeration("/root/b"));
        Assert.Equal(1, source.CallCount("/root/b"));
        Assert.Equal(0, source.CallCount("/root/link"));
        Assert.Equal(0, source.CallCount("/root/mount"));
    }

    [Fact]
    public void ShiftF3_IsUnavailableForVirtualPanel()
    {
        var source = new ControlledSource(PanelProviderCapabilities.Enumerate);
        using var services = Services(source);
        FilePanelState panel = PreparePanel(services, source, Dir(source, "/root/a"));
        panel.ContentKind = PanelContentKind.Virtual;

        Assert.False(services.CommandRegistry.CanExecute(FunctionKeyCommandIds.CalculateDirectorySizes, services.CommandContext));
        services.CommandRegistry.Execute(FunctionKeyCommandIds.CalculateDirectorySizes, services.CommandContext);
        Thread.Sleep(30);
        Assert.Empty(source.Calls);
    }

    private ApplicationServices Services(ControlledSource source)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;
        var registry = new FilePanelSourceRegistry([
            new LocalFilePanelSource(fs),
            source,
        ]);

        return ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            sourceRegistry: registry);
    }

    private static FilePanelState PreparePanel(
        ApplicationServices services,
        ControlledSource source,
        params FilePanelItem[] items)
    {
        FilePanelState panel = services.CommandContext.LeftPanel;
        panel.CurrentLocation = new PanelLocation(source.SourceId, "/root");
        panel.ProviderCapabilities = source.Capabilities;
        panel.ContentKind = PanelContentKind.Source;
        panel.Items.Clear();
        panel.Items.AddRange(items);
        panel.CursorIndex = 0;
        services.CommandContext.ActiveSide = PanelSide.Left;
        return panel;
    }

    private static FilePanelItem Dir(ControlledSource source, string path, FileAttributes extra = 0) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = true,
        Attributes = FileAttributes.Directory | extra,
    };

    private static FilePanelItem Mount(ControlledSource source, string path) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = true,
        Attributes = FileAttributes.Directory,
        IsVolumeMountPoint = true,
    };

    private static FilePanelItem File(ControlledSource source, string path, long size) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = false,
        Size = size,
        Attributes = FileAttributes.Normal,
    };

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private sealed class ControlledSource : IFilePanelSource
    {
        private readonly ConcurrentDictionary<string, IReadOnlyList<FilePanelItem>> _items = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ManualResetEventSlim> _started = new(StringComparer.Ordinal);
        private int _enumerationOrdinal;

        public ControlledSource(PanelProviderCapabilities capabilities)
        {
            SourceId = new PanelSourceId($"dir-size-command-{Guid.NewGuid():N}");
            Capabilities = capabilities;
        }

        public PanelSourceId SourceId { get; }
        public string DisplayName => SourceId.Value;
        public PanelProviderCapabilities Capabilities { get; }
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public ConcurrentBag<string> Calls { get; } = [];
        public bool BlockEnumeration { get; init; }
        public bool BlockFirstEnumeration { get; init; }
        public ManualResetEventSlim ReleaseEnumeration { get; } = new(false);

        public void Set(string path, params FilePanelItem[] items) => _items[path] = items;
        public int CallCount(string path) => Calls.Count(value => value == path);
        public bool WaitForEnumeration(string path) =>
            _started.GetOrAdd(path, _ => new ManualResetEventSlim(false)).Wait(TimeSpan.FromSeconds(3));

        public string NormalizePath(string sourcePath)
        {
            string path = sourcePath.TrimEnd('/');
            return path.Length == 0 ? "/" : path;
        }

        public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";
        public string? GetParentPath(string sourcePath) => null;

        public IReadOnlyList<FilePanelItem> EnumerateDirectory(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            Calls.Add(path);
            _started.GetOrAdd(path, _ => new ManualResetEventSlim(false)).Set();
            int ordinal = Interlocked.Increment(ref _enumerationOrdinal);
            if (BlockEnumeration || (BlockFirstEnumeration && ordinal == 1))
                WaitHandle.WaitAny([ReleaseEnumeration.WaitHandle, cancellationToken.WaitHandle], TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            return _items.TryGetValue(path, out IReadOnlyList<FilePanelItem>? items) ? items : [];
        }

        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => null;
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
