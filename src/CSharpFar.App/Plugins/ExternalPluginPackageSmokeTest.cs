using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

/// <summary>Headless published-host smoke probe; never called in normal startup.</summary>
public static class ExternalPluginPackageSmokeTest
{
    public static async Task<int> VerifyAsync(string configDirectory)
    {
        // A release-tag version belongs to the executable host, not to the SDK.
        // This probes the real assemblies loaded by the published single-file app.
        var expectedSdkVersion = new Version(2, 0, 0, 0);
        if (typeof(ICSharpFarPlugin).Assembly.GetName().Version != expectedSdkVersion ||
            typeof(CSharpFar.Core.Abstractions.IFilePanelSource).Assembly.GetName().Version != expectedSdkVersion)
        {
            System.Console.Error.WriteLine("Published host changed the v2 SDK assembly identity.");
            return 1;
        }

        var discovery = ExternalPluginLoader.Discover(configDirectory);
        foreach (string error in discovery.Errors)
            System.Console.Error.WriteLine(error);
        if (discovery.Errors.Count != 0 || discovery.Plugins.Count != 2)
            return 1;

        var ui = new SmokeUi();
        var runtime = new NativePluginRuntime(
            new SmokeContext(),
            _ => new ApplicationPluginSettings(configDirectory),
            _ => ui,
            _ => new SmokeCredentials());

        bool success = true;
        try
        {
            foreach (var (descriptor, plugin) in discovery.Plugins)
            {
                var result = await runtime.RegisterAsync(descriptor, plugin);
                if (!result.Success)
                {
                    System.Console.Error.WriteLine(result.Error);
                    success = false;
                }
            }
            if (success)
            {
                runtime.Seal();
                foreach (string prefix in runtime.CommandPrefixes)
                {
                    if (!runtime.TryResolvePrefix(prefix, out var address, out _))
                    {
                        success = false;
                        break;
                    }
                    var result = await runtime.ExecuteAsync(
                        address, PluginCommandOrigin.CommandLine, PanelSide.Left);
                    if (result.Kind != PluginCommandResultKind.Completed)
                    {
                        System.Console.Error.WriteLine($"Extension command '{prefix}': {result.Error}");
                        success = false;
                    }
                }
            }
        }
        finally
        {
            await runtime.DisposeAsync();
        }

        bool verified = success &&
            ui.Messages.Any(message => message.Contains("1.0.0", StringComparison.Ordinal)) &&
            ui.Messages.Any(message => message.Contains("2.0.0", StringComparison.Ordinal)) &&
            File.Exists(Path.Combine(configDirectory, "plugins", "hello-world", "disposed.marker"));
        System.Console.WriteLine(verified
            ? "External plugin self-test passed: two DLLs, isolated versions, commands and disposal."
            : "External plugin self-test failed.");
        return verified ? 0 : 1;
    }

    // Verify the same discovery path as normal startup, with an isolated config.
    // Do not execute plugin commands (which may open UI or access external services).
    public static async Task<int> VerifyBundledAsync(string configDirectory, string[] expectedIds)
    {
        var discovery = ExternalPluginLoader.DiscoverForApplication(configDirectory);
        foreach (string error in discovery.Errors)
            System.Console.Error.WriteLine(error);
        if (discovery.Errors.Count != 0 ||
            !discovery.Plugins.Select(item => item.Descriptor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expectedIds) ||
            discovery.Plugins.Count != expectedIds.Length)
            return 1;

        var runtime = new NativePluginRuntime(new SmokeContext(),
            _ => new ApplicationPluginSettings(configDirectory), _ => new SmokeUi(), _ => new SmokeCredentials());
        try
        {
            foreach (var (descriptor, plugin) in discovery.Plugins)
            {
                var result = await runtime.RegisterAsync(descriptor, plugin);
                if (!result.Success)
                {
                    System.Console.Error.WriteLine(result.Error);
                    return 1;
                }
            }
            runtime.Seal();
            discovery.Manager.Complete(runtime, []);
            if (discovery.Manager.Entries.Any(entry => entry.Status != ExternalPluginStatus.Loaded))
                return 1;
            if (ExternalPluginLoader.DiscoverForApplication(configDirectory, noExtensions: true).Plugins.Count != 0)
                return 1;
            System.Console.WriteLine("Bundled extension self-test passed: " + string.Join(", ", expectedIds));
            return 0;
        }
        finally { await runtime.DisposeAsync(); }
    }

    private sealed class SmokeContext : IPluginApplicationContext
    {
        public PluginRunMode RunMode => PluginRunMode.Normal;
        public PanelSide ActivePanelSide => PanelSide.Left;
        public string CurrentDirectory => "/";
    }

    private sealed class SmokeUi : IPluginUiService
    {
        public List<string> Messages { get; } = [];
        public void ShowMessage(string title, string message) => Messages.Add(title + ": " + message);
        public string? Input(string title, string prompt, string? initialText = null) => null;
        public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) => null;
    }

    private sealed class SmokeCredentials : IPluginCredentialService
    {
        public string? TryReadPassword(string credentialId) => null;
        public void SavePassword(string credentialId, string password) { }
        public void DeletePassword(string credentialId) { }
    }
}
