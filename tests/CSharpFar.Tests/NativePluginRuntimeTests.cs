using CSharpFar.App.Plugins;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class NativePluginRuntimeTests
{
    [Fact]
    public async Task Registration_ResolvesCommandsMenusAndPrefixesWithoutSpecializedApplicationHandlers()
    {
        var app = new TestApplication();
        var runtime = CreateRuntime(app);
        IPluginCommandContext? captured = null;
        var plugin = new TestPlugin((host, _) =>
        {
            // Menu references are deliberately allowed to precede command registration.
            host.RegisterMenuItem(new PluginMenuItemDescriptor
            {
                Id = "menu", Title = "Example", CommandId = "open",
            });
            host.RegisterMenuItem(new PluginMenuItemDescriptor
            {
                Id = "disk", Title = "Example disk", CommandId = "open",
                Placement = PluginMenuPlacement.Disk,
            });
            host.RegisterCommandPrefix("Ex", "open");
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "open", Title = "Open",
                ExecuteAsync = (context, _) =>
                {
                    captured = context;
                    return ValueTask.FromResult(PluginCommandResult.Completed());
                },
            });
            return ValueTask.CompletedTask;
        });

        var result = await runtime.RegisterAsync(Descriptor("example"), plugin);
        Assert.True(result.Success, result.Error);
        runtime.Seal();

        Assert.Equal(["menu", "disk"], runtime.MenuItems.Select(i => i.Item.Id).ToArray());
        Assert.True(runtime.TryResolvePrefix("eX:abc def", out var address, out string arguments));
        Assert.Equal("abc def", arguments);
        Assert.Equal(new PluginCommandAddress("example", "open"), address);

        var execution = await runtime.ExecuteAsync(
            address, PluginCommandOrigin.CommandLine, PanelSide.Right, "eX:abc def", arguments);
        Assert.Equal(PluginCommandResultKind.Completed, execution.Kind);
        Assert.NotNull(captured);
        Assert.Equal(PanelSide.Right, captured.TargetPanelSide);
        Assert.Equal(PluginCommandOrigin.CommandLine, captured.Origin);
        Assert.Equal("eX:abc def", captured.RawCommandLine);
        Assert.Equal("abc def", captured.Arguments);

        // The active side can change independently of the immutable invocation snapshot.
        app.ActivePanelSide = PanelSide.Left;
        Assert.Equal(PanelSide.Right, captured.TargetPanelSide);
        Assert.Throws<InvalidOperationException>(() =>
            runtime.RegisterAsync(Descriptor("late"), plugin).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task InvalidOrFailedRegistration_IsAtomicAndDoesNotBlockIndependentPlugins()
    {
        var runtime = CreateRuntime(new TestApplication());
        var valid = new TestPlugin((host, _) =>
        {
            host.RegisterCommand(Command("command"));
            host.RegisterCommandPrefix("shared", "command");
            host.RegisterMenuItem(Menu("entry", "command"));
            return ValueTask.CompletedTask;
        });
        Assert.True((await runtime.RegisterAsync(Descriptor("one"), valid)).Success);

        var duplicate = new TestPlugin((host, _) =>
        {
            host.RegisterCommand(Command("command"));
            host.RegisterMenuItem(Menu("invisible", "command"));
            host.RegisterCommandPrefix("SHARED", "command");
            return ValueTask.CompletedTask;
        });
        var rejected = await runtime.RegisterAsync(Descriptor("two"), duplicate);
        Assert.False(rejected.Success);
        Assert.Contains("Duplicate", rejected.Error);
        Assert.DoesNotContain(runtime.MenuItems, item => item.PluginId == "two");

        var failsDuringInitialization = new TestPlugin((host, _) =>
        {
            host.RegisterMenuItem(Menu("partial", "later"));
            throw new InvalidOperationException("intended failure");
        });
        Assert.False((await runtime.RegisterAsync(Descriptor("broken"), failsDuringInitialization)).Success);

        var unknownReference = new TestPlugin((host, _) =>
        {
            host.RegisterMenuItem(Menu("invalid", "missing"));
            return ValueTask.CompletedTask;
        });
        Assert.False((await runtime.RegisterAsync(Descriptor("bad-ref"), unknownReference)).Success);

        var another = new TestPlugin((host, _) =>
        {
            host.RegisterCommand(Command("command"));
            host.RegisterMenuItem(Menu("visible", "command"));
            return ValueTask.CompletedTask;
        });
        Assert.True((await runtime.RegisterAsync(Descriptor("other"), another)).Success);
        Assert.Equal(["one", "other"], runtime.MenuItems.Select(item => item.PluginId).ToArray());
        Assert.True(runtime.TryResolvePrefix("shared", out var address, out _));
        Assert.Equal("one", address.PluginId);
    }

    [Fact]
    public async Task IncompleteValueTask_IsAwaitedAndExceptionsAndCancellationAreOutcomes()
    {
        var runtime = CreateRuntime(new TestApplication());
        var tcs = new TaskCompletionSource<PluginCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new TestPlugin((host, _) =>
        {
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "async", Title = "Async",
                ExecuteAsync = (_, _) => new ValueTask<PluginCommandResult>(tcs.Task),
            });
            host.RegisterCommand(new PluginCommandDescriptor
            {
                Id = "failed", Title = "Failed",
                ExecuteAsync = (_, _) => throw new InvalidOperationException("expected"),
            });
            return ValueTask.CompletedTask;
        });
        Assert.True((await runtime.RegisterAsync(Descriptor("async-test"), plugin)).Success);

        var pending = runtime.ExecuteAsync(new("async-test", "async"),
            PluginCommandOrigin.PluginsMenu, PanelSide.Left);
        Assert.False(pending.IsCompleted);
        tcs.SetResult(PluginCommandResult.Completed());
        Assert.Equal(PluginCommandResultKind.Completed, (await pending).Kind);

        var failed = await runtime.ExecuteAsync(new("async-test", "failed"),
            PluginCommandOrigin.PluginsMenu, PanelSide.Left);
        Assert.Equal(PluginCommandResultKind.Failed, failed.Kind);
        Assert.Contains("expected", failed.Error);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = await runtime.ExecuteAsync(new("async-test", "async"),
            PluginCommandOrigin.PluginsMenu, PanelSide.Left, cancellationToken: cts.Token);
        Assert.Equal(PluginCommandResultKind.Cancelled, cancelled.Kind);
    }

    [Fact]
    public async Task DemoHost_IsolatedSettingsAndCredentialsDoNotTouchRealConfiguration()
    {
        string realDirectory = Path.Combine(Path.GetTempPath(), "CSharpFarRealSettings_" + Guid.NewGuid().ToString("N"));
        var realCredentials = new TestCredentials();
        var runtime = CreateRuntime(new TestApplication { RunMode = PluginRunMode.Demo },
            new TestSettings(realDirectory), realCredentials);
        string? demoDirectory = null;
        var plugin = new TestPlugin((host, _) =>
        {
            Assert.Equal(PluginRunMode.Demo, host.Application.RunMode);
            demoDirectory = host.Settings.GetSettingsDirectory("demo-plugin");
            Assert.NotEqual(realDirectory, demoDirectory);
            Assert.Throws<UnauthorizedAccessException>(() => host.Settings.GetSettingsDirectory("other"));
            Assert.Throws<UnauthorizedAccessException>(() => host.Credentials.SavePassword("password", "secret"));
            host.RegisterCommand(Command("safe"));
            return ValueTask.CompletedTask;
        });

        Assert.True((await runtime.RegisterAsync(Descriptor("demo-plugin"), plugin)).Success);
        Assert.NotNull(demoDirectory);
        Assert.DoesNotContain(realDirectory, demoDirectory!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(realDirectory));
        Assert.Equal(0, realCredentials.Writes);
        Assert.Equal(PluginCommandResultKind.Completed,
            (await runtime.ExecuteAsync(new("demo-plugin", "safe"),
                PluginCommandOrigin.PluginsMenu, PanelSide.Left)).Kind);
        Directory.Delete(demoDirectory!, recursive: true);
    }

    private static PluginDescriptor Descriptor(string id) => new() { Id = id, Title = id };

    private static PluginCommandDescriptor Command(string id) => new()
    {
        Id = id, Title = id,
        ExecuteAsync = (_, _) => ValueTask.FromResult(PluginCommandResult.Completed()),
    };

    private static PluginMenuItemDescriptor Menu(string id, string commandId) =>
        new() { Id = id, Title = id, CommandId = commandId };

    private static NativePluginRuntime CreateRuntime(
        TestApplication app, TestSettings? settings = null, TestCredentials? credentials = null)
        => new(app,
            _ => settings ?? new TestSettings(Path.Combine(Path.GetTempPath(), "CSharpFarPluginTests")),
            _ => new TestUi(),
            _ => credentials ?? new TestCredentials());

    private sealed class TestPlugin(Func<IPluginHost, CancellationToken, ValueTask> initialize) : ICSharpFarPlugin
    {
        public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken) =>
            initialize(host, cancellationToken);
    }

    private sealed class TestApplication : IPluginApplicationContext
    {
        public PluginRunMode RunMode { get; init; }
        public PanelSide ActivePanelSide { get; set; } = PanelSide.Left;
        public string CurrentDirectory { get; } = "/";
    }

    private sealed class TestSettings(string directory) : IPluginSettingsService
    {
        public string GetSettingsDirectory(string pluginId) => directory;
    }

    private sealed class TestCredentials : IPluginCredentialService
    {
        public int Writes { get; private set; }
        public string? TryReadPassword(string credentialId) => null;
        public void SavePassword(string credentialId, string password) => Writes++;
        public void DeletePassword(string credentialId) => Writes++;
    }

    private sealed class TestUi : IPluginUiService
    {
        public void ShowMessage(string title, string message) { }
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }
}
