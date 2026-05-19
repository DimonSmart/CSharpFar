using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.Dialogs;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class ApplicationVolumeTests : IDisposable
{
    private readonly string _tempDir;

    public ApplicationVolumeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarVolumeTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Alt+F1 changes left panel ─────────────────────────────────────────────

    [Fact]
    public void AltF1_SelectVolume_ChangesLeftPanelDirectory()
    {
        string volPath = Path.Combine(_tempDir, "VolC");
        Directory.CreateDirectory(volPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "C:\\", DisplayName = "C:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready,
            TotalBytes = 2_000_000_000L, FreeBytes = 700_000_000L, Shortcut = "C",
        });

        var driver = new FakeConsoleDriver();
        // Alt+F1 → Enter (select first) → F10 (quit)
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(volPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void AltF1_SelectVolume_RestartsWatcherForNewDirectory()
    {
        string volPath = Path.Combine(_tempDir, "VolWatch");
        Directory.CreateDirectory(volPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "W:\\", DisplayName = "W:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready,
            Shortcut = "W",
        });

        var watcher = new RecordingWatcher();
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService, watcher, new LocalLocationService());
        app.Run();

        Assert.Contains(watcher.StartRequests,
            r => r.PanelSide == PanelSide.Left &&
                 string.Equals(r.DirectoryPath, volPath, StringComparison.OrdinalIgnoreCase));
    }


    // ── Alt+F2 changes right panel ────────────────────────────────────────────

    [Fact]
    public void AltF2_SelectVolume_ChangesRightPanelDirectory()
    {
        string volPath = Path.Combine(_tempDir, "VolD");
        Directory.CreateDirectory(volPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "D:\\", DisplayName = "D:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "D",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(volPath, GetRightPanel(app).CurrentDirectory);
    }

    [Fact]
    public void AltF1_SelectVolume_UsesSameVolumeDirectoryFromRightPanel()
    {
        string volPath = Path.Combine(_tempDir, "VolSame");
        string rightSubDirectory = Path.Combine(volPath, "Projects");
        Directory.CreateDirectory(rightSubDirectory);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "S:\\", DisplayName = "S:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "S",
        });
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);
        fs.AddDirectory(volPath);
        fs.AddDirectory(rightSubDirectory);
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = rightSubDirectory;
        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            volumeService: volService);

        app.Run();

        Assert.Equal(rightSubDirectory, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Alt+F1 does NOT change right panel ───────────────────────────────────

    [Fact]
    public void AltF1_SelectVolume_DoesNotChangeRightPanel()
    {
        string volPath = Path.Combine(_tempDir, "VolC2");
        Directory.CreateDirectory(volPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "C:\\", DisplayName = "C:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "C",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(_tempDir, GetRightPanel(app).CurrentDirectory);
    }

    // ── Esc closes without change ─────────────────────────────────────────────

    [Fact]
    public void AltF1_Esc_DoesNotChangeLeftPanel()
    {
        string originalDir = _tempDir;

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "C:\\", DisplayName = "C:", RootPath = @"C:\",
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "C",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(originalDir, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Shortcut selects unique volume ────────────────────────────────────────

    [Fact]
    public void AltF1_ShortcutKey_SelectsMatchingVolume()
    {
        string volCPath = Path.Combine(_tempDir, "ShortC");
        string volDPath = Path.Combine(_tempDir, "ShortD");
        Directory.CreateDirectory(volCPath);
        Directory.CreateDirectory(volDPath);

        var volService = new FakeVolumeService(
            new FileSystemVolume
            {
                Id = "C:\\", DisplayName = "C:", RootPath = volCPath,
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "C",
            },
            new FileSystemVolume
            {
                Id = "D:\\", DisplayName = "D:", RootPath = volDPath,
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "D",
            });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        // Press 'd' — should select volume D (unique shortcut)
        driver.EnqueueKey(new ConsoleKeyInfo('d', ConsoleKey.D, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(volDPath, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Unavailable volume shows error, no directory change ──────────────────

    [Fact]
    public void AltF1_UnavailableVolume_ShowsErrorAndKeepsDirectory()
    {
        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "Y:\\", DisplayName = "Y:", RootPath = @"Y:\",
            Kind = VolumeKind.Network, Status = VolumeStatus.Disconnected, Shortcut = "Y",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        // MessageDialog waits for Enter/Esc
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        // Close DriveDialog
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(_tempDir, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Error on one volume does not hide others ──────────────────────────────

    [Fact]
    public void GetVolumes_ErrorOnOneVolume_OthersStillPresent()
    {
        string goodPath = Path.Combine(_tempDir, "GoodVol");
        Directory.CreateDirectory(goodPath);

        var volService = new FakeVolumeService(
            new FileSystemVolume
            {
                Id = "E:\\", DisplayName = "E:", RootPath = @"E:\",
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Error, Shortcut = "E",
            },
            new FileSystemVolume
            {
                Id = "F:\\", DisplayName = "F:", RootPath = goodPath,
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "F",
            });

        var volumes = volService.GetVolumes();

        Assert.Equal(2, volumes.Count);
        Assert.Contains(volumes, v => v.DisplayName == "E:" && v.Status == VolumeStatus.Error);
        Assert.Contains(volumes, v => v.DisplayName == "F:" && v.Status == VolumeStatus.Ready);
    }

    // ── Unix-like RootPath displayed without drive-letter assumption ──────────

    [Fact]
    public void AltF1_UnixLikeRootPath_NavigatesCorrectly()
    {
        string mountPath = Path.Combine(_tempDir, "mnt_data");
        Directory.CreateDirectory(mountPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = mountPath, DisplayName = "data", RootPath = mountPath,
            Kind = VolumeKind.MountPoint, Status = VolumeStatus.Ready, Shortcut = null,
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(mountPath, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Initial cursor matches current panel directory (spec tests #13/#14) ───

    [Fact]
    public void AltF1_OpenDialog_CursorOnCurrentPanelVolume()
    {
        // Two volumes: C (index 0) and D (index 1).
        // Left panel is "on D:\" — so the cursor should start on D (index 1).
        string cPath = Path.Combine(_tempDir, "VolC_Init");
        string dPath = Path.Combine(_tempDir, "VolD_Init");
        string dSub  = Path.Combine(dPath, "Projects");
        Directory.CreateDirectory(cPath);
        Directory.CreateDirectory(dSub);

        var volService = new FakeVolumeService(
            new FileSystemVolume
            {
                Id = "C:\\", DisplayName = "C:", RootPath = cPath,
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "C",
            },
            new FileSystemVolume
            {
                Id = "D:\\", DisplayName = "D:", RootPath = dPath,
                Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "D",
            });

        var driver = new FakeConsoleDriver();
        // Open dialog (cursor should be on D:), press Enter immediately.
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var fs = new FakeFileSystemService();
        fs.AddDirectory(dSub);
        fs.AddDirectory(dPath);
        fs.AddDirectory(cPath);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory  = dSub;  // left panel is inside D:
        settings.Panels.RightStartDirectory = _tempDir;

        var app = new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            volumeService: volService);

        app.Run();

        // Enter with cursor on D: should navigate to dPath (D:'s RootPath)
        Assert.Equal(dPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void AltF1_OpenDialog_NoMatchingVolume_CursorOnFirstItem()
    {
        string volPath = Path.Combine(_tempDir, "SomeVol");
        Directory.CreateDirectory(volPath);

        // Volume whose RootPath does NOT match _tempDir (the panel's current dir)
        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "X:\\", DisplayName = "X:", RootPath = volPath,
            Kind = VolumeKind.Fixed, Status = VolumeStatus.Ready, Shortcut = "X",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        // Cursor falls back to first item (X:), so Enter navigates to volPath
        Assert.Equal(volPath, GetLeftPanel(app).CurrentDirectory);
    }

    // ── Network Unchecked volume is selectable (spec test #10) ───────────────

    [Fact]
    public void AltF1_NetworkUncheckedVolume_IsSelectable()
    {
        string netPath = Path.Combine(_tempDir, "NetShare");
        Directory.CreateDirectory(netPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "P:\\", DisplayName = "P:", RootPath = netPath,
            Kind = VolumeKind.Network, Status = VolumeStatus.Unchecked,
            TotalBytes = null, FreeBytes = null, Shortcut = "P",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(netPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void AltF1_NetworkUncheckedVolume_ShortcutSelectsImmediately()
    {
        string netPath = Path.Combine(_tempDir, "NetShare2");
        Directory.CreateDirectory(netPath);

        var volService = new FakeVolumeService(new FileSystemVolume
        {
            Id = "Z:\\", DisplayName = "Z:", RootPath = netPath,
            Kind = VolumeKind.Network, Status = VolumeStatus.Unchecked,
            TotalBytes = null, FreeBytes = null, Shortcut = "Z",
        });

        var driver = new FakeConsoleDriver();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, shift: false, alt: true,  control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('z', ConsoleKey.Z,  shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        var app = CreateApp(driver, volService);
        app.Run();

        Assert.Equal(netPath, GetLeftPanel(app).CurrentDirectory);
    }

    [Fact]
    public void DriveDialog_RendersVolumeRowsWithFarLikeMenuColors()
    {
        var driver = new FakeConsoleDriver(width: 64, height: 18);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        var screen = new ScreenRenderer(driver);
        var items = new[]
        {
            new VolumeSelectionItem
            {
                Label = "C:",
                Shortcut = "C",
                Action = VolumeSelectionAction.OpenVolume,
                Volume = new FileSystemVolume
                {
                    Id = "C:\\",
                    DisplayName = "C:",
                    RootPath = "C:\\",
                    Kind = VolumeKind.Fixed,
                    Status = VolumeStatus.Ready,
                    TotalBytes = 2_000_000_000_000L,
                    FreeBytes = 700_000_000_000L,
                    Shortcut = "C",
                },
            },
            new VolumeSelectionItem
            {
                Label = "D:",
                Shortcut = "D",
                Action = VolumeSelectionAction.OpenVolume,
                Volume = new FileSystemVolume
                {
                    Id = "D:\\",
                    DisplayName = "D:",
                    RootPath = "D:\\",
                    Kind = VolumeKind.Fixed,
                    Status = VolumeStatus.Ready,
                    Shortcut = "D",
                },
            },
        };

        new DriveDialog(screen).Show(items);

        string text = string.Join(Environment.NewLine, driver.WriteRecords.Select(r => r.Text));
        Assert.Contains("Disk", text);
        Assert.Contains("Free", text);
        Assert.Contains("Total", text);
        Assert.Contains('│', text);
        Assert.Contains('┼', text);
        Assert.Contains('╔', text);
        string topFrameRow = driver.WriteRecords.First(r => r.Text.Contains('╔')).Text;
        int topFrameX = topFrameRow.IndexOf('╔');
        Assert.True(topFrameX > 0);
        Assert.Equal(' ', topFrameRow[topFrameX - 1]);
        Assert.Contains(driver.WriteRecords,
            r => r.Text.Contains("C: fixed", StringComparison.Ordinal) &&
                 r.Foreground == ConsoleColor.Yellow);
        Assert.Contains(driver.WriteRecords,
            r => r.Text.Contains("D: fixed", StringComparison.Ordinal) &&
                 r.Foreground == ConsoleColor.Yellow);
        Assert.Contains(driver.WriteRecords,
            r => r.Text.Contains("Change drive", StringComparison.Ordinal) &&
                 r.Foreground == ConsoleColor.White &&
                 r.Background == ConsoleColor.DarkCyan);
    }

    // ── KindLabel for Unchecked shows kind, not error (spec test #10) ─────────

    [Theory]
    [InlineData(VolumeKind.Network,    VolumeStatus.Unchecked, "network")]
    [InlineData(VolumeKind.Fixed,      VolumeStatus.Ready,     "fixed")]
    [InlineData(VolumeKind.Removable,  VolumeStatus.Ready,     "removable")]
    [InlineData(VolumeKind.Network,    VolumeStatus.Disconnected, "disconnected")]
    [InlineData(VolumeKind.Fixed,      VolumeStatus.NotReady,  "not ready")]
    [InlineData(VolumeKind.Fixed,      VolumeStatus.Error,     "error")]
    public void KindLabel_ReturnsExpectedText(VolumeKind kind, VolumeStatus status, string expected)
    {
        Assert.Equal(expected, DriveDialog.KindLabel(kind, status));
    }

    // ── WindowsVolumeService smoke test (tests #11/#12 at integration level) ──

    [Fact]
    public void WindowsVolumeService_GetVolumes_DoesNotThrow()
    {
        // Ensures the service runs on the current system without exceptions.
        // Also validates that network drives (if any) have Unchecked status with null sizes.
        var svc     = new CSharpFar.FileSystem.WindowsVolumeService();
        var volumes = svc.GetVolumes(); // must return quickly

        Assert.NotNull(volumes);
        foreach (var v in volumes.Where(v => v.Kind == VolumeKind.Network))
        {
            Assert.Equal(VolumeStatus.Unchecked, v.Status);
            Assert.Null(v.TotalBytes);
            Assert.Null(v.FreeBytes);
        }
    }

    // ── DriveDialog.FormatBytes ───────────────────────────────────────────────

    [Theory]
    [InlineData(659L  * 1024 * 1024 * 1024, "659 G")]
    [InlineData(21_474_836_480L,             "20,0 G")]   // 20 GB exactly
    [InlineData(2_000_000_000_000L,          "1,82 T")]
    [InlineData(1024L,                        "1,00 K")]
    [InlineData(100L * 1024,                  "100 K")]
    public void FormatBytes_ProducesExpectedOutput(long bytes, string expected)
    {
        Assert.Equal(expected, DriveDialog.FormatBytes(bytes));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Application CreateApp(
        FakeConsoleDriver driver,
        IVolumeService? volumeService = null,
        IFileSystemChangeWatcher? watcher = null,
        IFileSystemLocationService? locationService = null)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir);

        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory  = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            volumeService: volumeService,
            changeWatcher: watcher,
            locationService: locationService);
    }

    private static FilePanelState GetLeftPanel(Application app) =>
        GetField<FilePanelState>(app, "_left");

    private static FilePanelState GetRightPanel(Application app) =>
        GetField<FilePanelState>(app, "_right");

    private static T GetField<T>(object obj, string name)
    {
        var field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{name}' not found.");
        return (T)field.GetValue(obj)!;
    }

    // ── fakes ─────────────────────────────────────────────────────────────────

    private sealed class FakeVolumeService : IVolumeService
    {
        private readonly List<FileSystemVolume> _volumes;
        public FakeVolumeService(params FileSystemVolume[] volumes) => _volumes = [.. volumes];
        public IReadOnlyList<FileSystemVolume> GetVolumes() => _volumes;
    }

    private sealed class RecordingWatcher : IFileSystemChangeWatcher
    {
        public event EventHandler<FileSystemPanelChanged>? Changed;
        public List<PanelWatchRequest> StartRequests { get; } = [];

        public PanelAutoRefreshState StartWatching(PanelWatchRequest request)
        {
            StartRequests.Add(request);
            return new PanelAutoRefreshState { IsWatching = true };
        }

        public void StopWatching(PanelSide panelSide) { }
        public void Dispose() { }

        public void Raise(FileSystemPanelChanged change) =>
            Changed?.Invoke(this, change);
    }

    private sealed class LocalLocationService : IFileSystemLocationService
    {
        public FileSystemLocationInfo GetLocationInfo(string path) =>
            new()
            {
                Path = path,
                IsNetworkDrive = false,
                IsRemovableDrive = false,
                IsFixedDrive = true,
                RootPath = Path.GetPathRoot(path),
            };
    }

    private sealed class NoOpShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) { }
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
