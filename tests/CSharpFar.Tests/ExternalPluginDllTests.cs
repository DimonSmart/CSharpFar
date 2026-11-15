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
        string? assemblyName = null, int apiVersion = 1)
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
