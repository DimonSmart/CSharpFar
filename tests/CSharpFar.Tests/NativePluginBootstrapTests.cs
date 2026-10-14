using System.Reflection;
using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Modules;
using CSharpFar.App.Plugins;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class NativePluginBootstrapTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarPlugins_").FullName;

    [Fact]
    public async Task PublicFactory_PreservesExternalCompositionMenusPrefixAndOrder()
    {
        var invokedSide = PanelSide.Left;
        var first = new ScriptPlugin((host, _) =>
        {
            Register(host, "first", "First", "hello", side => invokedSide = side);
            return ValueTask.CompletedTask;
        });
        var second = new ScriptPlugin((host, _) =>
        {
            Register(host, "second", "Second", "world");
            return ValueTask.CompletedTask;
        });
        int enumerations = 0;
        IEnumerable<(PluginDescriptor, ICSharpFarPlugin)> Plugins()
        {
            enumerations++;
            yield return (Descriptor("one"), first);
            yield return (Descriptor("two"), second);
        }

        var app = Create(Plugins());
        Assert.Equal(1, enumerations);
        var catalog = Catalog(app);
        Assert.Contains(catalog.MenuItems, item => item.Text == "First");
        Assert.Contains(catalog.DiskMenuItems, item => item.Text == "Second disk");
        Assert.True(catalog.MenuItems.FindIndexForTest("First") < catalog.MenuItems.FindIndexForTest("Second"));
        Assert.True(catalog.TryOpenFromCommandLine("HeLlO:arg", PanelSide.Right, out var call));
        Assert.Equal(PluginCommandResultKind.Completed, (await call).Kind);
        Assert.Equal(PanelSide.Right, invokedSide);
        Assert.True(catalog.TryOpenFromCommandLine("WORLD:value", PanelSide.Left, out _));
        Assert.Contains(catalog.MenuItems, item => item.Text == "Processes and Ports...");
    }

    [Fact]
    public async Task CreateAsync_WaitsForInitializerAndMaintainsSequentialRegistration()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int secondStarted = 0;
        var slow = new ScriptPlugin(async (host, _) =>
        {
            Register(host, "slow", "Slow", "slow");
            await completion.Task;
        });
        var fast = new ScriptPlugin((host, _) =>
        {
            secondStarted++;
            Register(host, "fast", "Fast", "fast");
            return ValueTask.CompletedTask;
        });

        var creation = CreateAsync([(Descriptor("slow"), slow), (Descriptor("fast"), fast)]);
        Assert.False(creation.IsCompleted);
        Assert.Equal(0, secondStarted);
        completion.SetResult();
        var app = await creation;
        Assert.Equal(1, secondStarted);
        Assert.Contains(Catalog(app).MenuItems, item => item.Text == "Slow");
        Assert.Contains(Catalog(app).MenuItems, item => item.Text == "Fast");
    }

    [Fact]
    public async Task SyncCreate_RejectsPendingWithoutAllowingLatePublication()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stageProvider = new DisposableProvider(() => disposed.TrySetResult());
        var slow = new ScriptPlugin(async (host, _) =>
        {
            Register(host, "late", "Late", "late");
            host.RegisterPanelProvider(stageProvider);
            await completion.Task;
        });
        var error = Assert.Throws<InvalidOperationException>(() =>
            Create([(Descriptor("late"), slow)]));
        Assert.Contains("ApplicationFactory.CreateAsync", error.Message);
        completion.SetResult();
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void LocalFailure_IsolatedAndVisibleInBootstrapSummary()
    {
        var one = new ScriptPlugin((host, _) =>
        {
            Register(host, "one", "One", "one");
            return ValueTask.CompletedTask;
        });
        var broken = new ScriptPlugin((host, _) =>
        {
            Register(host, "broken", "Broken", "broken");
            throw new InvalidOperationException("failure inside plugin");
        });
        var three = new ScriptPlugin((host, _) =>
        {
            Register(host, "three", "Three", "three");
            return ValueTask.CompletedTask;
        });
        var app = Create([(Descriptor("one"), one), (Descriptor("broken"), broken), (Descriptor("three"), three)]);
        var catalog = Catalog(app);
        Assert.Contains(catalog.MenuItems, item => item.Text == "One");
        Assert.Contains(catalog.MenuItems, item => item.Text == "Three");
        Assert.DoesNotContain(catalog.MenuItems, item => item.Text == "Broken");
        var failures = (IReadOnlyList<string>)typeof(Application)
            .GetField("_startupPluginErrors", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!;
        Assert.Single(failures);
        Assert.Contains("failure inside plugin", failures[0]);
    }

    [Fact]
    public async Task GeneratedAndExplicitActionIdCollisionRejectsOnlyConflictingPlugin()
    {
        var runtime = new NativePluginRuntime(
            new TestContext(), _ => new TestSettings(), _ => new TestUi(), _ => new TestCredentials());
        var original = new PluginMenuItemDescriptor { Id = "entry", Title = "First", CommandId = "go" };
        var generated = PluginMenuActionIds.Get("first", original);
        var a = new ScriptPlugin((host, _) =>
        {
            host.RegisterCommand(Command("go"));
            host.RegisterMenuItem(original);
            return ValueTask.CompletedTask;
        });
        var b = new ScriptPlugin((host, _) =>
        {
            host.RegisterCommand(Command("go"));
            host.RegisterMenuItem(new PluginMenuItemDescriptor
            {
                Id = "conflict", Title = "Conflict", CommandId = "go", ActionId = generated,
            });
            return ValueTask.CompletedTask;
        });
        Assert.True((await runtime.RegisterAsync(Descriptor("first"), a)).Success);
        Assert.False((await runtime.RegisterAsync(Descriptor("second"), b)).Success);
        Assert.Single(runtime.MenuItems);
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task CancelledBootstrapDoesNotStartFollowingPlugin()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int following = 0;
        var slow = new ScriptPlugin(async (host, _) =>
        {
            started.SetResult();
            await completed.Task;
            Register(host, "slow", "Slow", "slow");
        });
        var next = new ScriptPlugin((host, _) =>
        {
            following++;
            return ValueTask.CompletedTask;
        });

        var task = CreateAsync([(Descriptor("slow"), slow), (Descriptor("next"), next)], cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        completed.SetResult();
        Assert.Equal(0, following);
    }

    [Fact]
    public async Task LocalCancellationContinuesWithNextPlugin()
    {
        var abortsItself = new ScriptPlugin((_, _) => throw new OperationCanceledException("local"));
        var after = new ScriptPlugin((host, _) =>
        {
            Register(host, "after", "After", "after");
            return ValueTask.CompletedTask;
        });
        var app = await CreateAsync([(Descriptor("local"), abortsItself), (Descriptor("after"), after)]);
        Assert.Contains(Catalog(app).MenuItems, item => item.Text == "After");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Application Create(IEnumerable<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> plugins)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        return ApplicationFactory.Create(
            new ScreenRenderer(new FakeConsoleDriver(width: 80, height: 25)),
            fs, new NoOpShellService(), new NoOpFileOperationService(),
            new InMemoryHistoryStore(), Settings(),
            enableBuiltInNetworkModules: false, configDirectory: _root,
            additionalPlugins: plugins);
    }

    private Task<Application> CreateAsync(
        IEnumerable<(PluginDescriptor Descriptor, ICSharpFarPlugin Plugin)> plugins,
        CancellationToken token = default)
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        return ApplicationFactory.CreateAsync(
            new ScreenRenderer(new FakeConsoleDriver(width: 80, height: 25)),
            fs, new NoOpShellService(), new NoOpFileOperationService(),
            new InMemoryHistoryStore(), Settings(),
            enableBuiltInNetworkModules: false, configDirectory: _root,
            additionalPlugins: plugins, cancellationToken: token);
    }

    private AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Application.ConfirmExit = false;
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;
        return settings;
    }

    private static NativeModuleCatalog Catalog(Application app) =>
        (NativeModuleCatalog)typeof(Application)
            .GetField("_moduleCatalog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static PluginDescriptor Descriptor(string id) => new() { Id = id, Title = id };
    private static PluginCommandDescriptor Command(string id) => new()
    {
        Id = id, Title = id, ExecuteAsync = (_, _) => ValueTask.FromResult(PluginCommandResult.Completed()),
    };

    private static void Register(IPluginHost host, string id, string title, string prefix,
        Action<PanelSide>? invoked = null)
    {
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = id, Title = title,
            ExecuteAsync = (context, _) =>
            {
                invoked?.Invoke(context.TargetPanelSide);
                return ValueTask.FromResult(PluginCommandResult.Completed());
            },
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor { Id = id, Title = title, CommandId = id });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = id + "-disk", Title = title + " disk", CommandId = id,
            Placement = PluginMenuPlacement.Disk,
        });
        host.RegisterCommandPrefix(prefix, id);
    }

    private sealed class ScriptPlugin(Func<IPluginHost, CancellationToken, ValueTask> initialize) : ICSharpFarPlugin
    {
        public ValueTask InitializeAsync(IPluginHost host, CancellationToken token) => initialize(host, token);
    }

    private sealed class TestContext : IPluginApplicationContext
    {
        public PluginRunMode RunMode => PluginRunMode.Normal;
        public PanelSide ActivePanelSide => PanelSide.Left;
        public string CurrentDirectory => "/";
    }

    private sealed class TestSettings : IPluginSettingsService
    {
        public string GetSettingsDirectory(string id) => Path.GetTempPath();
    }

    private sealed class TestUi : IPluginUiService
    {
        public void ShowMessage(string title, string message) { }
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }

    private sealed class TestCredentials : IPluginCredentialService
    {
        public string? TryReadPassword(string id) => null;
        public void SavePassword(string id, string password) { }
        public void DeletePassword(string id) { }
    }

    private sealed class DisposableProvider(Action dispose) : IPanelProvider, IDisposable
    {
        public string Id => "owned";
        public string Title => "Owned";
        public ValueTask<PluginPanelOpenRequest> OpenAsync(IPanelProviderContext context, CancellationToken token) =>
            throw new NotSupportedException();
        public void Dispose() => dispose();
    }
}

internal static class PluginMenuTestExtensions
{
    public static int FindIndexForTest(this IReadOnlyList<ModuleMenuProjection> values, string title) =>
        Array.FindIndex(values.ToArray(), item => item.Text == title);
}
