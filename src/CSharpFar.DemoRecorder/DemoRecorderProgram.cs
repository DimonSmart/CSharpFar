using System.Diagnostics;
using System.Text.Json;
using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.DemoRecorder;

internal static class DemoRecorderProgram
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            RecorderArguments options = RecorderArguments.Parse(args);
            Directory.CreateDirectory(options.OutputDirectory);

            string scenarioPath = Path.GetFullPath(options.ScenarioPath);
            string fixturePath = Path.GetFullPath(options.FixturePath);
            string outputDirectory = Path.GetFullPath(options.OutputDirectory);

            var scenario = DemoScenario.Load(scenarioPath);
            DirectoryFingerprint baseline = DirectoryFingerprint.Capture(fixturePath);

            string framesDirectory = Path.Combine(outputDirectory, "frames");
            if (Directory.Exists(framesDirectory))
                Directory.Delete(framesDirectory, recursive: true);
            Directory.CreateDirectory(framesDirectory);

            string screenshotPath = Path.Combine(outputDirectory, scenario.ScreenshotFileName);
            string gifPath = Path.Combine(outputDirectory, scenario.GifFileName);
            string mp4Path = Path.Combine(outputDirectory, scenario.Mp4FileName);
            DeleteIfExists(screenshotPath);
            DeleteIfExists(gifPath);
            DeleteIfExists(mp4Path);

            using var driver = new RecordingConsoleDriver(scenario.ViewportWidth, scenario.ViewportHeight)
            {
                IsSupported = true,
            };
            var renderer = new ScreenRenderer(driver);

            string tempRoot = Path.Combine(Path.GetTempPath(), "CSharpFar.DemoRecorder", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var app = CreateDemoApplication(renderer, driver, tempRoot, fixturePath);
                using var rasterizer = new SnapshotRasterizer(scenario.Render);
                var recorder = new DemoRecordingSession(
                    driver,
                    rasterizer,
                    new FrameSequenceWriter(framesDirectory),
                    scenario,
                    outputDirectory,
                    screenshotPath);

                driver.BeforeReadInput = _ => recorder.OnApplicationReady();
                app.Run();
                recorder.EnsureCompleted();

                DirectoryFingerprint after = DirectoryFingerprint.Capture(fixturePath);
                baseline.AssertEqual(after, fixturePath);

                await FfmpegArtifactBuilder.BuildAsync(
                    framesDirectory,
                    gifPath,
                    mp4Path,
                    scenario.FramesPerSecond,
                    cancellationToken);

                if (!File.Exists(screenshotPath))
                    throw new InvalidOperationException($"Recorder did not produce screenshot: {screenshotPath}");

                global::System.Console.WriteLine($"Scenario: {scenario.Name}");
                global::System.Console.WriteLine($"Screenshot: {screenshotPath}");
                global::System.Console.WriteLine($"GIF: {gifPath}");
                global::System.Console.WriteLine($"MP4: {mp4Path}");
                return 0;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or JsonException)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Application CreateDemoApplication(
        ScreenRenderer renderer,
        RecordingConsoleDriver driver,
        string tempRoot,
        string fixturePath)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = "/";
        settings.Panels.RightStartDirectory = "/";
        settings.History.MaxCommandHistoryItems = 32;
        settings.History.MaxDirectoryHistoryItems = 32;
        settings.History.MaxFileHistoryItems = 32;

        var source = DemoFilePanelSource.ImportFromDirectory(fixturePath);
        var sourceRegistry = new FilePanelSourceRegistry([source]);
        var fileOperations = new FileOperationService(sourceRegistry, new DisabledFileSystemPlatformOperations());
        var history = new InMemoryHistoryStore(
            settings.History.MaxCommandHistoryItems,
            settings.History.MaxDirectoryHistoryItems,
            settings.History.MaxFileHistoryItems);

        return ApplicationFactory.Create(
            renderer,
            new DisabledLocalFileSystemService(),
            new DisabledShellService(),
            fileOperations,
            history,
            settings,
            new UserMenuStore(tempRoot),
            saveSettings: null,
            volumeService: new DemoVolumeService(),
            volumeInfoService: null,
            changeWatcher: null,
            locationService: null,
            mountPointService: null,
            fileLauncher: new DisabledFileLauncher(),
            searchService: new DisabledSearchService(),
            sourceRegistry: sourceRegistry,
            credentialStore: new EmptyCredentialStore(),
            enableBuiltInNetworkModules: false,
            configDirectory: tempRoot,
            terminalScreenMode: driver,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixturePath));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class DisabledShellService : IShellService
    {
        public void Execute(string command, string workingDirectory) =>
            throw new InvalidOperationException("External commands are disabled in demo mode.");
    }

    private sealed class DisabledLocalFileSystemService : IFileSystemService
    {
        public IReadOnlyList<FilePanelItem> ReadDirectory(string path) => throw Disabled();
        public bool DirectoryExists(string path) => throw Disabled();
        public bool FileExists(string path) => throw Disabled();

        private static IOException Disabled() =>
            new("Local file system access is disabled in demo mode.");
    }

    private sealed class DisabledFileLauncher : IFileLauncher
    {
        public FileLaunchMode GetLaunchMode(string fullPath) => FileLaunchMode.ShellAssociation;

        public void OpenFile(string fullPath, string workingDirectory) =>
            throw new InvalidOperationException("External file launching is disabled in demo mode.");
    }

    private sealed class DisabledFileSystemPlatformOperations : IFileSystemPlatformOperations
    {
        public bool SupportsRecycleBin => false;
        public void DeleteFile(string path, bool useRecycleBin) => throw Disabled();
        public void DeleteDirectory(string path, bool recursive, bool useRecycleBin) => throw Disabled();
        public bool IsSymbolicLink(string path) => throw Disabled();
        public bool TryCopySymbolicLink(string sourcePath, string destinationPath, out string? error) => throw Disabled();

        public void PreserveFileMetadata(
            string sourcePath,
            string destinationPath,
            FileOperationOptions options,
            IFileOperationErrorSink errors) => throw Disabled();

        private static IOException Disabled() =>
            new("Local file system operations are disabled in demo mode.");
    }

    private sealed class EmptyCredentialStore : ICredentialStore
    {
        public void SavePassword(string credentialId, string password) { }
        public string? TryReadPassword(string credentialId) => null;
        public void DeletePassword(string credentialId) { }
    }

    private sealed class DemoVolumeService : IVolumeService
    {
        private readonly IReadOnlyList<FileSystemVolume> _volumes =
        [
            new FileSystemVolume
            {
                Id = "demo",
                DisplayName = "[DEMO] /",
                RootPath = "/",
                Kind = VolumeKind.Pseudo,
                Status = VolumeStatus.Ready,
                Shortcut = "0",
            },
        ];

        public IReadOnlyList<FileSystemVolume> GetVolumes() => _volumes;
    }

    private sealed class DisabledSearchService : ISearchService
    {
        public IAsyncEnumerable<SearchResultItem> SearchAsync(
            SearchRequest request,
            IProgress<SearchProgress>? progress,
            CancellationToken cancellationToken = default) =>
            new DisabledSearchEnumerable(cancellationToken);

        private sealed class DisabledSearchEnumerable(CancellationToken cancellationToken)
            : IAsyncEnumerable<SearchResultItem>, IAsyncEnumerator<SearchResultItem>
        {
            public SearchResultItem Current => null!;

            public IAsyncEnumerator<SearchResultItem> GetAsyncEnumerator(CancellationToken token = default) =>
                new DisabledSearchEnumerable(token.CanBeCanceled ? token : cancellationToken);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<bool> MoveNextAsync()
            {
                if (cancellationToken.IsCancellationRequested)
                    return ValueTask.FromCanceled<bool>(cancellationToken);

                return ValueTask.FromException<bool>(
                    new IOException("Local filesystem search is disabled in this composition."));
            }
        }
    }
}

internal sealed record RecorderArguments(string FixturePath, string ScenarioPath, string OutputDirectory)
{
    public static RecorderArguments Parse(string[] args)
    {
        string fixture = Path.Combine("docs", "demo", "filesystem");
        string scenario = Path.Combine("scripts", "demo", "readme-demo.json");
        string output = Path.Combine("artifacts", "demo");

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--fixture":
                    fixture = NextValue(args, ref i, arg);
                    break;
                case "--scenario":
                    scenario = NextValue(args, ref i, arg);
                    break;
                case "--output":
                    output = NextValue(args, ref i, arg);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument: {arg}");
            }
        }

        return new RecorderArguments(fixture, scenario, output);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
            throw new InvalidOperationException($"Missing value for {option}.");

        index++;
        return args[index];
    }
}
