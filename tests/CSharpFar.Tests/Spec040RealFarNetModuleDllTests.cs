using System.Resources;
using CSharpFar.App;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.FarNetHost;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

[Collection(FarNetTestCollection.Name)]
public sealed class Spec040RealFarNetModuleDllTests : IDisposable
{
    private const string OfficialModuleName = "CSharpFar.Tests.Fixtures.OfficialFarNetModule";

    private static readonly Guid OfficialToolId =
        Guid.Parse("6e2e88d2-42ba-4828-b086-0c00d4f887db");
    private static readonly Guid OfficialDiskToolId =
        Guid.Parse("1ceadce7-02d7-4470-8227-7b0d1894a6f4");
    private static readonly Guid OfficialPanelToolId =
        Guid.Parse("f37012dd-6d46-4de0-8991-2d7c55bc0ac7");

    private readonly string _tempDir;
    private readonly List<IDisposable> _disposables = [];

    public Spec040RealFarNetModuleDllTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec040_{Guid.NewGuid():N}");
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
    public void OfficialFarNetCompiledDll_LoadsThroughShimAndReadsResources()
    {
        var host = CreateHost(resourceMode: ResourceMode.Valid);

        Assert.Contains(host.MenuItems, item => item.ActionId == OfficialToolId && item.Text == "Official Real Tool");
        Assert.Contains(host.DiskMenuItems, item => item.ActionId == OfficialDiskToolId && item.Text == "Official disk");
        Assert.Contains("ofn", host.CommandPrefixes);
        Assert.Contains("ofnp", host.CommandPrefixes);
        Assert.Contains("ofncp", host.CommandPrefixes);
        Assert.DoesNotContain(host.Diagnostics, diagnostic => diagnostic.Message.Contains("FarNet", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocalRealFarNetDllInModuleDirectory_IsIgnored()
    {
        var host = CreateHost(resourceMode: ResourceMode.Valid, includeLocalRealFarNetDll: true);

        Assert.Contains(host.MenuItems, item => item.ActionId == OfficialToolId);
        Assert.DoesNotContain(host.Diagnostics, diagnostic => diagnostic.Message.Contains("cannot cast", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OfficialTool_InvokesMessageInputAndManagerFolder()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromMenu(OfficialToolId);

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("official-seed", StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(
            _tempDir,
            "settings",
            "FarNet",
            "local",
            OfficialModuleName)));
    }

    [Theory]
    [InlineData(true, "left")]
    [InlineData(false, "right")]
    public void OfficialDiskTool_ReceivesDiskSide(bool isLeft, string expectedText)
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromDiskMenu(OfficialDiskToolId, isLeft);

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains(expectedText, StringComparison.Ordinal));
    }

    [Fact]
    public void OfficialPanelTool_OpensReadOnlyModulePanelWhenEnabled()
    {
        var host = CreateHost(resourceMode: ResourceMode.Valid, enablePanelTools: true);
        host.Initialize(CreateHostServices(new FakeConsoleDriver()));

        var result = host.OpenFromMenu(OfficialPanelToolId);

        Assert.Equal(FarNetModuleOpenResultKind.OpenedPanel, result.Kind);
        Assert.NotNull(result.Panel);
        Assert.Equal("Official panel", result.Panel.DisplayName);
        Assert.Equal(PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.Refresh, result.Panel.Capabilities);

        var files = result.Panel.EnumerateDirectory("/");
        var file = Assert.Single(files);
        Assert.Equal("alpha.txt", file.Name);
        Assert.Equal("/alpha.txt", file.FullPath);
        Assert.Equal(5, file.Size);
    }

    [Fact]
    public void OfficialCommand_DispatchesByPrefix()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromCommandLine("ofn:payload");

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("ofn:payload", StringComparison.Ordinal));
    }

    [Fact]
    public void OfficialPanelCommand_ReturnsOpenedPanel()
    {
        var host = CreateHost(resourceMode: ResourceMode.Valid, enablePanelTools: true);
        host.Initialize(CreateHostServices(new FakeConsoleDriver()));

        var result = host.OpenFromCommandLine("ofnp:");

        Assert.Equal(FarNetModuleOpenResultKind.OpenedPanel, result.Kind);
        Assert.NotNull(result.Panel);
        Assert.Equal("Official panel", result.Panel.DisplayName);
    }

    [Fact]
    public void OfficialPanelCommand_DefaultHostOptionsReturnOpenedPanel()
    {
        var host = CreateHostWithDefaultOptions(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(new FakeConsoleDriver()));

        var result = host.OpenFromCommandLine("ofnp:");

        Assert.Equal(FarNetModuleOpenResultKind.OpenedPanel, result.Kind);
        Assert.NotNull(result.Panel);
        Assert.Equal("Official panel", result.Panel.DisplayName);
    }

    [Fact]
    public void OfficialCommandParametersCommand_ParsesSubcommandAndParameters()
    {
        string samplePath = Path.Combine(_tempDir, "sample.json");
        File.WriteAllText(samplePath, "{}");
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var host = CreateHost(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(driver));

        var result = host.OpenFromCommandLine($"ofncp:parse file={samplePath}; flag=true");

        Assert.Equal(FarNetModuleOpenResultKind.Completed, result.Kind);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("sample.json|True", StringComparison.Ordinal));
    }

    [Fact]
    public void OfficialCommandParametersCommand_UnknownParameterIsControlledFailure()
    {
        string samplePath = Path.Combine(_tempDir, "sample.json");
        File.WriteAllText(samplePath, "{}");
        var host = CreateHost(resourceMode: ResourceMode.Valid);
        host.Initialize(CreateHostServices(new FakeConsoleDriver()));

        var result = host.OpenFromCommandLine($"ofncp:parse file={samplePath}; extra=1");

        Assert.Equal(FarNetModuleOpenResultKind.Failed, result.Kind);
        Assert.Contains("Unknown parameter", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("extra", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Application_DispatchesFarNetCommandBeforeShell()
    {
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var shell = new RecordingShellService();
        var app = CreateApplication(driver, shell, enablePanelTools: false);

        app.ExecuteCommand("ofn:payload");

        Assert.Empty(shell.Commands);
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("ofn:payload", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_FarNetCommandCanOpenModulePanel()
    {
        var shell = new RecordingShellService();
        var app = CreateApplication(new FakeConsoleDriver(), shell, enablePanelTools: true);

        app.ExecuteCommand("ofnp:");

        Assert.Empty(shell.Commands);
        Assert.Equal("Official panel", app.ActiveState.DisplayTitle);
        var item = Assert.Single(app.ActiveState.Items);
        Assert.Equal("alpha.txt", item.Name);
    }

    [Fact]
    public void InvalidResources_BecomeDiagnosticsAndMenuFallsBackToKey()
    {
        var host = CreateHost(resourceMode: ResourceMode.Invalid);

        Assert.Contains(host.MenuItems, item => item.ActionId == OfficialToolId && item.Text == "OfficialToolTitle");
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("invalid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(host.MenuItems, item => item.ActionId == FarNetModuleIds.DiagnosticsActionId);
    }

    [Fact]
    public void MissingResources_BecomeDiagnosticsAndMenuFallsBackToKey()
    {
        var host = CreateHost(resourceMode: ResourceMode.Missing);

        Assert.Contains(host.MenuItems, item => item.ActionId == OfficialToolId && item.Text == "OfficialToolTitle");
        Assert.Contains(host.Diagnostics, diagnostic => diagnostic.Message.Contains("Resource file", StringComparison.OrdinalIgnoreCase));
    }

    private FarNetModuleHost CreateHost(
        ResourceMode resourceMode,
        bool includeLocalRealFarNetDll = true,
        bool enablePanelTools = false)
    {
        string modulesRoot = Path.Combine(_tempDir, "FarNet", "Modules");
        string moduleDirectory = Path.Combine(modulesRoot, OfficialModuleName);
        Directory.CreateDirectory(moduleDirectory);

        CopyOfficialFixture(moduleDirectory, includeLocalRealFarNetDll);
        WriteResources(moduleDirectory, resourceMode);

        var host = new FarNetModuleHost(new FarNetModuleHostOptions
        {
            ModulesRoot = modulesRoot,
            EnablePanelTools = enablePanelTools,
        });
        _disposables.Add(host);
        return host;
    }

    private FarNetModuleHost CreateHostWithDefaultOptions(
        ResourceMode resourceMode,
        bool includeLocalRealFarNetDll = true)
    {
        string modulesRoot = Path.Combine(_tempDir, "FarNet", "Modules");
        string moduleDirectory = Path.Combine(modulesRoot, OfficialModuleName);
        Directory.CreateDirectory(moduleDirectory);

        CopyOfficialFixture(moduleDirectory, includeLocalRealFarNetDll);
        WriteResources(moduleDirectory, resourceMode);

        var host = new FarNetModuleHost(new FarNetModuleHostOptions
        {
            ModulesRoot = modulesRoot,
        });
        _disposables.Add(host);
        return host;
    }

    private Application CreateApplication(
        FakeConsoleDriver driver,
        RecordingShellService shell,
        bool enablePanelTools)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            shell,
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            farNetModuleHost: CreateHost(ResourceMode.Valid, enablePanelTools: enablePanelTools),
            enableBuiltInNetworkModules: false,
            configDirectory: _tempDir);
    }

    private static void CopyOfficialFixture(string moduleDirectory, bool includeLocalRealFarNetDll)
    {
        string sourceDirectory = GetOfficialFixtureOutputDirectory();
        foreach (string sourcePath in Directory.GetFiles(sourceDirectory, "*.dll"))
        {
            if (!includeLocalRealFarNetDll &&
                string.Equals(Path.GetFileName(sourcePath), "FarNet.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetPath = Path.Combine(moduleDirectory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
    }

    private static void WriteResources(string moduleDirectory, ResourceMode resourceMode)
    {
        string resourcePath = Path.Combine(moduleDirectory, OfficialModuleName + ".resources");
        if (resourceMode == ResourceMode.Missing)
            return;

        if (resourceMode == ResourceMode.Invalid)
        {
            File.WriteAllText(resourcePath, "not a resources file");
            return;
        }

        using var writer = new ResourceWriter(resourcePath);
        writer.AddResource("OfficialToolTitle", "Official Real Tool");
        writer.Generate();
    }

    private static string GetOfficialFixtureOutputDirectory()
    {
        string repoRoot = GetRepoRoot();
        string configuration = GetBuildConfiguration();
        string outputDirectory = Path.Combine(
            repoRoot,
            "tests",
            "CSharpFar.Tests.Fixtures.OfficialFarNetModule",
            "bin",
            configuration,
            "net10.0");

        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException(outputDirectory);

        return outputDirectory;
    }

    private static string GetBuildConfiguration()
    {
        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        return baseDirectory.Parent?.Name is { Length: > 0 } configuration
            ? configuration
            : "Debug";
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CSharpFar.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot find repository root.");
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

    private enum ResourceMode
    {
        Valid,
        Invalid,
        Missing,
    }

    private sealed class RecordingShellService : IShellService
    {
        public List<(string Command, string WorkingDirectory)> Commands { get; } = [];

        public void Execute(string command, string workingDirectory) =>
            Commands.Add((command, workingDirectory));
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
