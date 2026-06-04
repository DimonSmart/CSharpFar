using System.Reflection;
using System.Xml.Linq;
using CSharpFar.App;
using CSharpFar.App.Commands;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Fixtures.FarNetDependency;
using CSharpFar.Tests.Fixtures.FarNetModule;

namespace CSharpFar.Tests;

public sealed class Spec037PluginSystemTests : IDisposable
{
    private static readonly Guid FarNetDiskToolId =
        Guid.Parse("2e2ee555-7153-4c4a-a73b-79b38b42c5d4");

    private readonly string _tempDir;

    public Spec037PluginSystemTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec037_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        DeleteTempDirectory();
    }

    [Fact]
    public void MenuProvider_ProjectsNativeModuleMenuItems()
    {
        var actionId = Guid.Parse("d5eed145-392c-4eb9-81d0-66ed90c53ad6");

        var menu = new DefaultMenuDefinitionProvider().BuildMenu(new MenuBuildContext
        {
            ActivePanelSide = PanelSide.Left,
            LeftPanel = new FilePanelState { CurrentDirectory = _tempDir },
            RightPanel = new FilePanelState { CurrentDirectory = _tempDir },
            LeftViewMode = PanelViewMode.Full,
            RightViewMode = PanelViewMode.Full,
            Settings = new AppSettings(),
            CanSaveSettings = false,
            ModuleMenuItems = [new ModuleMenuProjection(actionId, "FarNet tool", 'F')],
        });

        var moduleItem = menu.Items.Single(item => item.Text == "Plugins")
            .Children.Single(item => item.Text == "FarNet tool");
        Assert.Equal(MenuCommandIds.ModuleOpen, moduleItem.CommandId);
        var args = Assert.IsType<ModuleOpenCommandArgs>(moduleItem.CommandArgs);
        Assert.Equal(actionId, args.ActionId);
    }

    [Fact]
    public void Application_DispatchesNativeFarNetDiskMenuItem()
    {
        using var host = CreateFarNetModuleHost();
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var app = CreateApp(driver, farNetModuleHost: host);

        var result = app.OpenModuleDiskMenuItem(FarNetDiskToolId, PanelSide.Left);

        Assert.True(result.ShouldRender);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("left", StringComparison.Ordinal));
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

    private Application CreateApp(
        FakeConsoleDriver driver,
        FarNetModuleHost? farNetModuleHost = null)
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
            farNetModuleHost: farNetModuleHost,
            enableBuiltInNetworkModules: false,
            configDirectory: _tempDir);
    }

    private FarNetModuleHost CreateFarNetModuleHost()
    {
        string modulesRoot = Path.Combine(_tempDir, "FarNet", "Modules");
        string moduleName = typeof(MessageInputTool).Assembly.GetName().Name!;
        string moduleDirectory = Path.Combine(modulesRoot, moduleName);
        Directory.CreateDirectory(moduleDirectory);
        CopyAssembly(typeof(MessageInputTool).Assembly, moduleDirectory);
        CopyAssembly(typeof(MissingDependencyMarker).Assembly, moduleDirectory);

        return new FarNetModuleHost(modulesRoot);
    }

    private static void CopyAssembly(Assembly assembly, string targetDirectory)
    {
        string sourcePath = assembly.Location;
        string targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

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

    private void DeleteTempDirectory()
    {
        if (!Directory.Exists(_tempDir))
            return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(50);
            }
        }
    }
}
