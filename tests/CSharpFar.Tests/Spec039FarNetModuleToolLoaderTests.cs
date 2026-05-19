using System.Reflection;
using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.FarNetHost;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Fixtures.FarNetDependency;
using CSharpFar.Tests.Fixtures.FarNetModule;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

[Collection(FarNetTestCollection.Name)]
public sealed class Spec039FarNetModuleToolLoaderTests : IDisposable
{
    private static readonly Guid MessageInputToolId =
        Guid.Parse("e7ee66f9-a88f-4e69-bb20-1864c7015cbd");
    private static readonly Guid DiskToolId =
        Guid.Parse("2e2ee555-7153-4c4a-a73b-79b38b42c5d4");
    private static readonly Guid UnsupportedApiToolId =
        Guid.Parse("ace47cd5-15b9-4316-a0e6-e0fcbbca8dfd");
    private static readonly Guid DuplicateToolId =
        Guid.Parse("986c5308-7fa0-4bb6-bdcd-393317fe24ba");

    private readonly string _tempDir;
    private readonly List<IDisposable> _disposables = [];

    public Spec039FarNetModuleToolLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec039_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void MissingModulesRoot_ProducesNoMenuItemsOrDiagnostics()
    {
        var host = new FarNetModuleHost(Path.Combine(_tempDir, "missing"));

        Assert.Empty(host.MenuItems);
        Assert.Empty(host.DiskMenuItems);
        Assert.Empty(host.Diagnostics);
    }

    [Fact]
    public void ModuleHost_ProjectsSupportedToolsAndRecordsDiagnostics()
    {
        var host = CreateHost(copyDependency: true);

        Assert.Contains(host.MenuItems, item => item.ActionId == MessageInputToolId && item.Text == "Ask user");
        Assert.Contains(host.MenuItems, item => item.ActionId == UnsupportedApiToolId && item.Text == "Unsupported");
        Assert.Contains(host.MenuItems, item => item.ActionId == DuplicateToolId && item.Text == "Duplicate one");
        Assert.DoesNotContain(host.MenuItems, item => item.Text == "Invalid id");
        Assert.DoesNotContain(host.MenuItems, item => item.Text == "Config only");
        Assert.Contains(host.DiskMenuItems, item => item.ActionId == DiskToolId && item.Text == "Disk tool");
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("unsupported ModuleToolOptions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenFromMenu_InvokesMessageAndInputTool()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(copyDependency: true);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromMenu(MessageInputToolId);

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("seed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "left")]
    [InlineData(false, "right")]
    public void OpenFromDiskMenu_PassesDiskSideToModuleTool(bool isLeft, string expectedText)
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(copyDependency: true);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromDiskMenu(DiskToolId, isLeft);

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains(expectedText, StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedApi_ReturnsControlledFailure()
    {
        var host = CreateHost(copyDependency: true);
        host.Initialize(CreateHostServices(new FakeConsoleDriver()));

        var result = host.OpenFromMenu(UnsupportedApiToolId);

        Assert.Equal(FarNetModuleOpenResultKind.Failed, result.Kind);
        Assert.Contains("CreateListMenu", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDependency_IsRecordedWithoutBlockingOtherTools()
    {
        var host = CreateHost(copyDependency: false);

        Assert.Contains(host.MenuItems, item => item.ActionId == MessageInputToolId);
        Assert.Contains(
            host.Diagnostics,
            diagnostic => diagnostic.Message.Contains("CSharpFar.Tests.Fixtures.FarNetDependency", StringComparison.Ordinal));
    }

    [Fact]
    public void LocalFarNetDllInModuleDirectory_IsIgnored()
    {
        var host = CreateHost(copyDependency: true, includeInvalidLocalFarNetDll: true);

        Assert.Contains(host.MenuItems, item => item.ActionId == MessageInputToolId);
        Assert.DoesNotContain(host.Diagnostics, diagnostic => diagnostic.Message.Contains("Bad IL format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NewerFarNetAssemblyReference_IsNotSupported()
    {
        var supported = FarNetAssemblyCompatibility.SupportedVersion;
        var assemblyName = new AssemblyName("FarNet")
        {
            Version = new Version(supported.Major, supported.Minor, supported.Build + 1, supported.Revision),
        };

        Assert.False(FarNetAssemblyCompatibility.IsSupported(assemblyName));
    }

    private FarNetModuleHost CreateHost(
        bool copyDependency,
        bool includeInvalidLocalFarNetDll = false)
    {
        string modulesRoot = Path.Combine(_tempDir, "FarNet", "Modules");
        string moduleName = typeof(MessageInputTool).Assembly.GetName().Name!;
        string moduleDirectory = Path.Combine(modulesRoot, moduleName);
        Directory.CreateDirectory(moduleDirectory);

        CopyAssembly(typeof(MessageInputTool).Assembly, moduleDirectory);
        if (copyDependency)
            CopyAssembly(typeof(MissingDependencyMarker).Assembly, moduleDirectory);

        if (includeInvalidLocalFarNetDll)
            File.WriteAllText(Path.Combine(moduleDirectory, "FarNet.dll"), "not a real assembly");

        var host = new FarNetModuleHost(modulesRoot);
        _disposables.Add(host);
        return host;
    }

    private static void CopyAssembly(Assembly assembly, string targetDirectory)
    {
        string sourcePath = assembly.Location;
        string targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private FarNetModuleHostServices CreateHostServices(FakeConsoleDriver driver) =>
        new()
        {
            Ui = new ModuleUiServices
            {
                Screen = new ScreenRenderer(driver),
                Palette = () => PaletteRegistry.Default,
            },
            DataRoot = Path.Combine(_tempDir, "settings", "FarNet"),
            GetActivePanelSide = () => PanelSide.Left,
            GetPanelState = _ => new FilePanelState { CurrentDirectory = AppContext.BaseDirectory },
        };
}
