using System.Text.Json;
using CSharpFar.App.Plugins;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Tests;

public sealed class ExternalPluginDllTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarExternal_").FullName;

    [Fact]
    public async Task IndependentlyBuiltDlls_ResolveDistinctDependencyVersionsAndDispose()
    {
        if (!SamplesAvailable()) return;

        Install("a-hello", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        Install("b-goodbye", "goodbye", "goodbye-world", "GoodbyeWorld", "GoodbyeWorld.Plugin");
        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Empty(discovery.Errors);
        Assert.Equal(2, discovery.Plugins.Count);

        var ui = new RecordingUi();
        var runtime = CreateRuntime(ui);
        try
        {
            foreach (var (descriptor, plugin) in discovery.Plugins)
                Assert.True((await runtime.RegisterAsync(descriptor, plugin)).Success);
            runtime.Seal();

            Assert.Equal(2, runtime.MenuItems.Count);
            Assert.True(runtime.TryResolvePrefix("HELLO:example", out var hello, out _));
            Assert.Equal(PluginCommandResultKind.Completed,
                (await runtime.ExecuteAsync(hello, PluginCommandOrigin.CommandLine, PanelSide.Left)).Kind);
            Assert.True(runtime.TryResolvePrefix("goodbye", out var goodbye, out _));
            Assert.Equal(PluginCommandResultKind.Completed,
                (await runtime.ExecuteAsync(goodbye, PluginCommandOrigin.CommandLine, PanelSide.Right)).Kind);
            Assert.Contains(ui.Messages, message => message.Contains("1.0.0", StringComparison.Ordinal));
            Assert.Contains(ui.Messages, message => message.Contains("2.0.0", StringComparison.Ordinal));
            Assert.Equal("1", File.ReadAllText(Path.Combine(_root, "plugins", "hello-world", "counter.txt")));
        }
        finally
        {
            await runtime.DisposeAsync();
        }
        Assert.True(File.Exists(Path.Combine(_root, "plugins", "hello-world", "disposed.marker")));
    }

    [Fact]
    public async Task DamagedManifestAndMissingAssembly_AreLocalFailures()
    {
        if (!SamplesAvailable()) return;

        string extensions = Path.Combine(_root, "extensions");
        Directory.CreateDirectory(Path.Combine(extensions, "a-broken-json"));
        File.WriteAllText(Path.Combine(extensions, "a-broken-json", "plugin.json"), "{broken");
        WriteManifest("b-missing", "missing", "Missing", "Missing.Plugin", assemblyName: "Missing.dll");
        WriteManifest("c-unsupported", "unsupported", "Missing", "Missing.Plugin", apiVersion: 999);
        WriteManifest("d-traversal", "traversal", "Missing", "Missing.Plugin", assemblyName: "../Elsewhere.dll");
        Install("e-valid", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        Install("f-wrong-type", "hello", "wrong-type", "HelloWorld", "No.Such.Type");

        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Single(discovery.Plugins);
        Assert.Equal(5, discovery.Errors.Count);
        var runtime = CreateRuntime(new RecordingUi());
        try
        {
            foreach (var (descriptor, plugin) in discovery.Plugins)
                Assert.True((await runtime.RegisterAsync(descriptor, plugin)).Success);
            Assert.Single(runtime.MenuItems);
        }
        finally { await runtime.DisposeAsync(); }
    }

    [Fact]
    public async Task CaseInsensitiveDuplicateId_IsRejectedWithoutHarmingFirstPlugin()
    {
        if (!SamplesAvailable()) return;

        Install("a-first", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        Install("b-duplicate", "hello", "HELLO-WORLD", "HelloWorld", "HelloWorld.Plugin");
        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Empty(discovery.Errors);
        var runtime = CreateRuntime(new RecordingUi());
        try
        {
            Assert.True((await runtime.RegisterAsync(
                discovery.Plugins[0].Descriptor, discovery.Plugins[0].Plugin)).Success);
            var duplicate = await runtime.RegisterAsync(
                discovery.Plugins[1].Descriptor, discovery.Plugins[1].Plugin);
            Assert.False(duplicate.Success);
            Assert.Contains("case-insensitive", duplicate.Error);
            Assert.Single(runtime.MenuItems);
        }
        finally { await runtime.DisposeAsync(); }
    }

    [Fact]
    public async Task AsynchronousInitializationFailure_DoesNotBlockAnotherDll()
    {
        if (!SamplesAvailable()) return;

        Install("a-failing", "goodbye", "goodbye-world", "GoodbyeWorld", "GoodbyeWorld.Plugin");
        string settings = Path.Combine(_root, "plugins", "goodbye-world");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "fail-init"), "yes");
        Install("b-healthy", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Empty(discovery.Errors);
        var runtime = CreateRuntime(new RecordingUi());
        try
        {
            var results = new List<PluginInitializationResult>();
            foreach (var (descriptor, plugin) in discovery.Plugins)
                results.Add(await runtime.RegisterAsync(descriptor, plugin));
            Assert.False(results[0].Success);
            Assert.True(results[1].Success);
            Assert.Single(runtime.MenuItems);
            Assert.True(runtime.TryResolvePrefix("hello", out _, out _));
        }
        finally { await runtime.DisposeAsync(); }
    }


    [Fact]
    public async Task DisabledExtension_DoesNotExecuteAndCanBeReenabledWithoutLosingSettings()
    {
        if (!SamplesAvailable()) return;

        Install("a-hello", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        Install("b-goodbye", "goodbye", "goodbye-world", "GoodbyeWorld", "GoodbyeWorld.Plugin");

        var manager = ExternalPluginLoader.Discover(_root, noExtensions: true).Manager;
        var hello = Assert.Single(manager.Entries, item => item.FolderName == "a-hello");
        Assert.True(manager.TrySetEnabled(hello, enabled: false, out _));
        Assert.True(hello.RequiresRestart);

        var disabled = ExternalPluginLoader.Discover(_root);
        Assert.Empty(disabled.Errors);
        Assert.Single(disabled.Plugins);
        Assert.Equal("goodbye-world", disabled.Plugins[0].Descriptor.Id);
        Assert.Equal(ExternalPluginStatus.Disabled,
            Assert.Single(disabled.Manager.Entries, item => item.FolderName == "a-hello").Status);

        var runtime = CreateRuntime(new RecordingUi());
        try
        {
            foreach (var (descriptor, plugin) in disabled.Plugins)
                Assert.True((await runtime.RegisterAsync(descriptor, plugin)).Success);
            disabled.Manager.Complete(runtime, []);
            Assert.False(runtime.TryResolvePrefix("hello", out _, out _));
            Assert.True(runtime.TryResolvePrefix("goodbye", out _, out _));
            Assert.Equal(ExternalPluginStatus.Loaded,
                Assert.Single(disabled.Manager.Entries, item => item.FolderName == "b-goodbye").Status);
        }
        finally { await runtime.DisposeAsync(); }
        Assert.False(File.Exists(Path.Combine(_root, "plugins", "hello-world", "counter.txt")));

        string preserved = Path.Combine(_root, "plugins", "hello-world", "saved.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
        File.WriteAllText(preserved, "keep me");
        Assert.True(disabled.Manager.TrySetEnabled(
            Assert.Single(disabled.Manager.Entries, item => item.FolderName == "a-hello"),
            enabled: true, out _));

        var enabled = ExternalPluginLoader.Discover(_root);
        Assert.Equal(2, enabled.Plugins.Count);
        var restored = CreateRuntime(new RecordingUi());
        try
        {
            foreach (var (descriptor, plugin) in enabled.Plugins)
                Assert.True((await restored.RegisterAsync(descriptor, plugin)).Success);
            Assert.True(restored.TryResolvePrefix("hello", out _, out _));
        }
        finally { await restored.DisposeAsync(); }
        Assert.Equal("keep me", File.ReadAllText(preserved));
    }

    [Fact]
    public void BrokenManifest_CanBeDisabledByFolderName()
    {
        string folder = Path.Combine(_root, "extensions", "broken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{invalid json");

        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Single(discovery.Errors);
        var broken = Assert.Single(discovery.Manager.Entries);
        Assert.Equal(ExternalPluginStatus.Error, broken.Status);
        Assert.Equal("manifest", broken.FailureStage);
        Assert.Null(broken.Id);
        Assert.True(discovery.Manager.TrySetEnabled(broken, false, out _));

        var afterRestart = ExternalPluginLoader.Discover(_root);
        Assert.Empty(afterRestart.Errors);
        Assert.Empty(afterRestart.Plugins);
        Assert.Equal(ExternalPluginStatus.Disabled, Assert.Single(afterRestart.Manager.Entries).Status);
        Assert.Contains("broken", File.ReadAllText(Path.Combine(_root, "extensions-state.json")));
    }

    [Fact]
    public void NoExtensions_SkipsDllLoadingWithoutChangingConfiguration()
    {
        if (!SamplesAvailable()) return;
        Install("hello", "hello", "hello-world", "HelloWorld", "HelloWorld.Plugin");
        var normal = ExternalPluginLoader.Discover(_root);
        Assert.Single(normal.Plugins);

        var safe = ExternalPluginLoader.Discover(_root, noExtensions: true);
        Assert.Empty(safe.Plugins);
        Assert.Empty(safe.Errors);
        Assert.Single(safe.Manager.Entries);
        Assert.True(safe.Manager.Entries[0].Enabled);
        Assert.Equal(ExternalPluginStatus.Disabled, safe.Manager.Entries[0].Status);
        Assert.False(File.Exists(Path.Combine(_root, "extensions-state.json")));
    }

    [Fact]
    public void FailedStateWrite_DoesNotPretendToApplyChange()
    {
        string folder = Path.Combine(_root, "extensions", "broken");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{");
        var discovery = ExternalPluginLoader.Discover(_root);
        var item = Assert.Single(discovery.Manager.Entries);
        Directory.CreateDirectory(Path.Combine(_root, "extensions-state.json"));

        Assert.False(discovery.Manager.TrySetEnabled(item, false, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.True(item.Enabled);
        Assert.False(item.RequiresRestart);
    }

    [Fact]
    public void DisabledFolderState_IsCaseInsensitiveAndPreservedWhenFolderIsMissing()
    {
        File.WriteAllText(Path.Combine(_root, "extensions-state.json"),
            "{\"disabled\":[\"HELLO\",\"temporarily-absent\"]}");
        string folder = Path.Combine(_root, "extensions", "hello");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), "{");
        var discovery = ExternalPluginLoader.Discover(_root);
        Assert.Empty(discovery.Errors);
        Assert.Equal(ExternalPluginStatus.Disabled, Assert.Single(discovery.Manager.Entries).Status);
        Assert.True(discovery.Manager.TrySetEnabled(discovery.Manager.Entries[0], true, out _));
        string state = File.ReadAllText(Path.Combine(_root, "extensions-state.json"));
        Assert.DoesNotContain("HELLO", state, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporarily-absent", state);
    }

    [Fact]
    public void GlobalDisable_PersistsWithoutLosingIndividualChoicesOrLoadingAssemblies()
    {
        WriteManifest("one", "one", "Missing", "Missing.Plugin", assemblyName: "Missing.dll");
        WriteManifest("two", "two", "Missing", "Missing.Plugin", assemblyName: "Missing.dll");
        var manager = ExternalPluginLoader.Discover(_root, noExtensions: true).Manager;
        Assert.True(manager.TrySetEnabled(manager.Entries[0], false, out _));
        Assert.True(manager.TrySetExternalPluginsEnabled(false, out _));
        Assert.True(manager.GlobalRestartRequired);

        var disabled = ExternalPluginLoader.Discover(_root);
        Assert.Empty(disabled.Plugins);
        Assert.Empty(disabled.Errors); // Missing DLLs were not opened.
        Assert.All(disabled.Manager.Entries, entry => Assert.Equal(ExternalPluginStatus.Disabled, entry.Status));
        Assert.False(disabled.Manager.ExternalPluginsEnabled);
        Assert.True(disabled.Manager.TrySetExternalPluginsEnabled(true, out _));

        var restored = ExternalPluginLoader.Discover(_root);
        Assert.Equal(ExternalPluginStatus.Disabled, restored.Manager.Entries[0].Status);
        Assert.Equal(ExternalPluginStatus.Error, restored.Manager.Entries[1].Status);
        Assert.Single(restored.Errors);
    }

    [Fact]
    public void DevelopmentPackages_AreDiscoveredWithoutOverwritingUserPackages()
    {
        string development = Path.Combine(_root, "development-extensions");
        WriteManifest("same", "user", "Missing", "Missing.Plugin", assemblyName: "Missing.dll");
        WriteManifest("other", "other", "Missing", "Missing.Plugin", assemblyName: "Missing.dll");
        Directory.CreateDirectory(development);
        Directory.Move(Path.Combine(_root, "extensions", "other"), Path.Combine(development, "other"));
        Directory.CreateDirectory(Path.Combine(development, "same"));
        File.WriteAllText(Path.Combine(development, "same", "plugin.json"), "{}");

        var manager = ExternalPluginLoader.Discover(_root, true, development).Manager;
        Assert.Equal(2, manager.Entries.Count);
        Assert.Equal("user", manager.Entries.Single(entry => entry.FolderName == "same").Id);
        var other = manager.Entries.Single(entry => entry.FolderName == "other");
        Assert.True(manager.TrySetEnabled(other, false, out _));
        var restarted = ExternalPluginLoader.Discover(_root, false, development);
        Assert.Equal(ExternalPluginStatus.Disabled,
            restarted.Manager.Entries.Single(entry => entry.FolderName == "other").Status);
    }

    private NativePluginRuntime CreateRuntime(RecordingUi ui) => new(
        new ApplicationContext(),
        _ => new Settings(_root),
        _ => ui,
        _ => new Credentials());

    private void Install(string directory, string sample, string id, string assembly, string entryType)
    {
        string source = SamplePath(sample);
        string target = Path.Combine(_root, "extensions", directory);
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }
        WriteManifest(directory, id, assembly, entryType);
    }

    private void WriteManifest(string folder, string id, string assembly, string entryType,
        string? assemblyName = null, int apiVersion = 2)
    {
        string target = Path.Combine(_root, "extensions", folder);
        Directory.CreateDirectory(target);
        var manifest = new
        {
            id,
            name = id,
            version = "1.0.0",
            apiVersion,
            assembly = assemblyName ?? assembly + ".dll",
            entryType,
        };
        File.WriteAllText(Path.Combine(target, "plugin.json"), JsonSerializer.Serialize(manifest));
    }

    private static string SamplePath(string sample) => Environment.GetEnvironmentVariable(
        sample == "hello" ? "CSHARPFAR_EXTERNAL_HELLO" : "CSHARPFAR_EXTERNAL_GOODBYE")!;

    private static bool SamplesAvailable()
    {
        string? hello = Environment.GetEnvironmentVariable("CSHARPFAR_EXTERNAL_HELLO");
        string? goodbye = Environment.GetEnvironmentVariable("CSHARPFAR_EXTERNAL_GOODBYE");
        if (string.IsNullOrWhiteSpace(hello) && string.IsNullOrWhiteSpace(goodbye))
            return false; // Standalone binary samples are prepared by the platform CI job.
        Assert.True(File.Exists(Path.Combine(hello!, "HelloWorld.dll")));
        Assert.True(File.Exists(Path.Combine(goodbye!, "GoodbyeWorld.dll")));
        return true;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ApplicationContext : IPluginApplicationContext
    {
        public PluginRunMode RunMode => PluginRunMode.Normal;
        public PanelSide ActivePanelSide => PanelSide.Left;
        public string CurrentDirectory => "/";
    }

    private sealed class Settings(string root) : IPluginSettingsService
    {
        public string GetSettingsDirectory(string pluginId)
        {
            string path = Path.Combine(root, "plugins", pluginId);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private sealed class RecordingUi : IPluginUiService
    {
        public List<string> Messages { get; } = [];
        public void ShowMessage(string title, string message) => Messages.Add(title + ": " + message);
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }

    private sealed class Credentials : IPluginCredentialService
    {
        public string? TryReadPassword(string credentialId) => null;
        public void SavePassword(string credentialId, string password) { }
        public void DeletePassword(string credentialId) { }
    }
}
