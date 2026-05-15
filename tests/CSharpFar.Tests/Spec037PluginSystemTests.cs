using System.Reflection;
using System.Xml.Linq;
using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.Menu;
using CSharpFar.App.Plugins;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec037PluginSystemTests : IDisposable
{
    private readonly string _tempDir;

    public Spec037PluginSystemTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec037_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void PluginManager_RejectsDuplicatePluginIds()
    {
        var pluginId = Guid.Parse("84cf56e5-4877-4c1e-8130-958d9f7963dc");

        Assert.Throws<InvalidOperationException>(() => new PluginManager(
            [
                new RecordingPlugin(pluginId),
                new RecordingPlugin(pluginId),
            ],
            CreateStartupInfo()));
    }

    [Fact]
    public void PluginManager_RejectsDuplicatePluginMenuItemIds()
    {
        var itemId = Guid.Parse("af25476f-820c-4fec-8554-e957df21d761");
        var plugin = new RecordingPlugin(
            Guid.Parse("343d07c3-f9b6-4e3b-b618-3c73a9a5394a"),
            pluginMenuItems:
            [
                new PluginMenuItem { ItemId = itemId, Text = "One" },
                new PluginMenuItem { ItemId = itemId, Text = "Two" },
            ]);

        Assert.Throws<InvalidOperationException>(() => new PluginManager([plugin], CreateStartupInfo()));
    }

    [Fact]
    public void MenuProvider_ProjectsPluginMenuItems()
    {
        var pluginId = Guid.Parse("474cc664-8850-47c9-a451-96fe45924e7d");
        var itemId = Guid.Parse("a2ae83c6-abef-4240-aae2-8b113c7c8659");

        var menu = new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = new FilePanelState { CurrentDirectory = _tempDir },
            RightPanel = new FilePanelState { CurrentDirectory = _tempDir },
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.Full,
            Settings = new AppSettings(),
            CanSaveSettings = false,
            PluginMenuItems = [new PluginMenuProjection(pluginId, itemId, "Network...", 'N')],
        });

        var pluginItem = menu.Items.Single(item => item.Text == "Plugins")
            .Children.Single(item => item.Text == "Network...");
        Assert.Equal(MenuCommandIds.PluginOpen, pluginItem.CommandId);
        var args = Assert.IsType<PluginOpenCommandArgs>(pluginItem.CommandArgs);
        Assert.Equal(pluginId, args.PluginId);
        Assert.Equal(itemId, args.ItemId);
    }

    [Theory]
    [InlineData(ConsoleKey.F1, PluginOpenFrom.LeftDiskMenu)]
    [InlineData(ConsoleKey.F2, PluginOpenFrom.RightDiskMenu)]
    public void DriveSelection_DispatchesPluginDiskMenuItems(
        ConsoleKey functionKey,
        PluginOpenFrom expectedOpenFrom)
    {
        var plugin = new RecordingPlugin(
            Guid.Parse("58ac4021-6c3a-44ac-a514-bf6fdfbb92ec"),
            diskMenuItems:
            [
                new PluginMenuItem
                {
                    ItemId = Guid.Parse("6ec5190e-3c45-4013-b82d-df13d56772a7"),
                    Text = "Network",
                    HotKey = 'N',
                },
            ]);
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', functionKey, shift: false, alt: true, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('n', ConsoleKey.N, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, plugin);
        app.Run();

        var openInfo = Assert.Single(plugin.OpenCalls);
        Assert.Equal(expectedOpenFrom, openInfo.OpenFrom);
        Assert.Equal(expectedOpenFrom == PluginOpenFrom.LeftDiskMenu ? PanelSide.Left : PanelSide.Right, openInfo.PanelSide);
    }

    [Fact]
    public void CoreAssembly_DoesNotContainProtocolConnectionContracts()
    {
        var typeNames = typeof(PanelSourceId).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SftpConnectionInfo", typeNames);
        Assert.DoesNotContain("FtpConnectionInfo", typeNames);
        Assert.DoesNotContain("ISftpConnectionStore", typeNames);
        Assert.DoesNotContain("IFtpConnectionStore", typeNames);
        Assert.DoesNotContain(typeof(PanelSourceId).GetMethods(BindingFlags.Public | BindingFlags.Static), method => method.Name is "Sftp" or "Ftp");
        Assert.DoesNotContain(typeof(VolumeSelectionItem).GetProperties(), property => property.Name.Contains("Sftp", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(VolumeSelectionItem).GetProperties(), property => property.Name.Contains("Ftp", StringComparison.Ordinal));
    }

    [Fact]
    public void FileSystemProject_DoesNotReferenceProtocolPackages()
    {
        string repoRoot = FindRepoRoot();
        var project = XDocument.Load(Path.Combine(repoRoot, "src", "CSharpFar.FileSystem", "CSharpFar.FileSystem.csproj"));
        var packageNames = project
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("SSH.NET", packageNames);
        Assert.DoesNotContain("FluentFTP", packageNames);
    }

    private Application CreateApp(FakeConsoleDriver driver, params ICSharpFarPlugin[] plugins)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            plugins: plugins,
            configDirectory: _tempDir);
    }

    private PluginStartupInfo CreateStartupInfo() =>
        new()
        {
            Ui = new PluginUiServices
            {
                Screen = new ScreenRenderer(new FakeConsoleDriver()),
                Palette = () => PaletteRegistry.Default,
            },
            Settings = new PluginSettingsService(_tempDir),
            Panels = new FakePluginPanelHost(),
        };

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class RecordingPlugin : ICSharpFarPlugin
    {
        private readonly Guid _pluginId;
        private readonly IReadOnlyList<PluginMenuItem> _pluginMenuItems;
        private readonly IReadOnlyList<PluginMenuItem> _diskMenuItems;

        public RecordingPlugin(
            Guid pluginId,
            IReadOnlyList<PluginMenuItem>? pluginMenuItems = null,
            IReadOnlyList<PluginMenuItem>? diskMenuItems = null)
        {
            _pluginId = pluginId;
            _pluginMenuItems = pluginMenuItems ??
            [
                new PluginMenuItem
                {
                    ItemId = Guid.Parse("046be828-8aa4-4da1-924e-dc1eb3f3d49c"),
                    Text = "Plugin...",
                },
            ];
            _diskMenuItems = diskMenuItems ?? [];
        }

        public List<PluginOpenInfo> OpenCalls { get; } = [];

        public PluginGlobalInfo GetGlobalInfo() =>
            new()
            {
                PluginId = _pluginId,
                Title = "Recording plugin",
            };

        public PluginInfo GetPluginInfo() =>
            new()
            {
                PluginMenuItems = _pluginMenuItems,
                DiskMenuItems = _diskMenuItems,
            };

        public void SetStartupInfo(PluginStartupInfo startupInfo)
        {
        }

        public PluginOpenResult Open(PluginOpenInfo openInfo)
        {
            OpenCalls.Add(openInfo);
            return PluginOpenResult.Completed();
        }
    }

    private sealed class FakePluginPanelHost : IPluginPanelHost
    {
        public PanelSide ActiveSide => PanelSide.Left;

        public FilePanelState GetPanelState(PanelSide panelSide) =>
            new() { CurrentDirectory = AppContext.BaseDirectory };

        public void OpenPanel(PanelSide panelSide, IPluginPanel panel)
        {
        }

        public void RefreshPanels()
        {
        }
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory)
        {
        }
    }

    private sealed class NoOpFileOperationService : IFileOperationService
    {
        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
    }
}
