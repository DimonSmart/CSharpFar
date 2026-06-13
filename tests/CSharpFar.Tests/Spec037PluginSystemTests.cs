using System.Reflection;
using System.Xml.Linq;
using CSharpFar.App.Commands;
using CSharpFar.App.Menu;
using CSharpFar.App.Modules;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

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
            ModuleMenuItems = [new ModuleMenuProjection(actionId, "Network plugin", 'N')],
        });

        var moduleItem = menu.Items.Single(item => item.Text == "Plugins")
            .Children.Single(item => item.Text == "Network plugin");
        Assert.Equal(MenuCommandIds.ModuleOpen, moduleItem.CommandId);
        var args = Assert.IsType<ModuleOpenCommandArgs>(moduleItem.CommandArgs);
        Assert.Equal(actionId, args.ActionId);
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
