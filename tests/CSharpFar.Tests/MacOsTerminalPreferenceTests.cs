using System.Diagnostics;
using System.Text.Json;
using CSharpFar.App.Settings;
using CSharpFar.Core.Models;
using CSharpFar.Host.MacOs;

namespace CSharpFar.Tests;

public sealed class MacOsTerminalPreferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CSharpFar-launch-" + Guid.NewGuid().ToString("N"));

    public MacOsTerminalPreferenceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("  ", "auto")]
    [InlineData("auto", "auto")]
    [InlineData("KITTY", "kitty")]
    [InlineData(" terminal ", "terminal")]
    [InlineData("unknown", "auto")]
    public void NormalizeUsesCanonicalValues(string? input, string expected) =>
        Assert.Equal(expected, MacOsTerminalPreference.Normalize(input));

    [Theory]
    [InlineData("{}", "auto")]
    [InlineData("{ \"application\": null }", "auto")]
    [InlineData("{ \"application\": {} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": \" KITTY \"} }", "kitty")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": \"terminal\"} }", "terminal")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": \"invalid\"} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": 42} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": true} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": []} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": {}} }", "auto")]
    [InlineData("{ \"application\": {\"preferredMacOsTerminal\": null} }", "auto")]
    public void ReadOnlyAndNormalStoreAgreeOnInvalidProperty(string json, string expected)
    {
        string config = NewPortableConfig();
        File.WriteAllText(Path.Combine(config, "settings.json"), json);
        string executableDirectory = Path.GetDirectoryName(config)!;
        Assert.Equal(expected, JsonSettingsStore.ReadMacOsTerminalPreference(executableDirectory));
        Assert.Equal(expected, new JsonSettingsStore(config).Settings.Application.PreferredMacOsTerminal);
    }

    [Fact]
    public void MissingConfigDoesNotCreateSettings()
    {
        string executableDirectory = Path.Combine(_root, "missing");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "CSharpFar.portable"), "");
        Assert.Equal("auto", JsonSettingsStore.ReadMacOsTerminalPreference(executableDirectory));
        Assert.False(Directory.Exists(Path.Combine(executableDirectory, "CSharpFar.config")));
    }

    [Fact]
    public void ReadOnlyIgnoresUnrelatedBrokenSections()
    {
        string config = NewPortableConfig();
        File.WriteAllText(Path.Combine(config, "settings.json"),
            "{\"application\":{\"preferredMacOsTerminal\":\"kitty\"},\"shell\":[123]}");
        Assert.Equal("kitty", JsonSettingsStore.ReadMacOsTerminalPreference(Path.GetDirectoryName(config)!));
    }

    [Fact]
    public void InvalidFileIsNotOverwrittenAndSelectsTerminal()
    {
        string config = NewPortableConfig();
        string filename = Path.Combine(config, "settings.json");
        string corrupt = "{ malformed json";
        File.WriteAllText(filename, corrupt);
        var diagnostics = new List<string>();
        Assert.Equal("terminal", JsonSettingsStore.ReadMacOsTerminalPreference(
            Path.GetDirectoryName(config)!, diagnostics.Add));
        Assert.Single(diagnostics);
        Assert.Equal(corrupt, File.ReadAllText(filename));
    }

    [Fact]
    public void ReadErrorFallsBackToTerminalWithoutWriting()
    {
        string exe = Path.Combine(_root, "x");
        string? error = null;
        Assert.Equal("terminal", JsonSettingsStore.ReadMacOsTerminalPreference(
            exe, _ => throw new UnauthorizedAccessException(), s => error = s));
        Assert.Contains("Terminal.app", error);
        Assert.Equal("auto", JsonSettingsStore.ReadMacOsTerminalPreference(
            exe, _ => throw new FileNotFoundException()));
    }

    [Fact]
    public void StoreRoundTripCanonicalizesAndWritesCamelCase()
    {
        string config = NewPortableConfig();
        var store = new JsonSettingsStore(config);
        Assert.Equal("auto", store.Settings.Application.PreferredMacOsTerminal);
        store.Settings.Application.PreferredMacOsTerminal = " KITTY ";
        store.Save();
        string json = File.ReadAllText(Path.Combine(config, "settings.json"));
        using var document = JsonDocument.Parse(json);
        Assert.Equal("kitty", document.RootElement
            .GetProperty("application").GetProperty("preferredMacOsTerminal").GetString());
        Assert.Equal("kitty", new JsonSettingsStore(config).Settings.Application.PreferredMacOsTerminal);
    }

    [Fact]
    public void PortableConfigurationIsSharedWithLauncher()
    {
        string config = NewPortableConfig();
        new JsonSettingsStore(config).Save();
        Assert.Equal(config, JsonSettingsStore.ResolveConfigDirectory(Path.GetDirectoryName(config)!));
        Assert.Equal("auto", JsonSettingsStore.ReadMacOsTerminalPreference(Path.GetDirectoryName(config)!));
    }

    private string NewPortableConfig()
    {
        string exe = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Resources");
        Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(exe, "CSharpFar.portable"), "");
        string config = JsonSettingsStore.ResolveConfigDirectory(exe);
        Directory.CreateDirectory(config);
        return config;
    }
}

public sealed class MacOsTerminalLauncherTests
{
    private static readonly string Home = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "home space ü"));

    private static readonly string Resources = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(), "CSharpFar copy ü.app", "Contents", "Resources"));

    private static readonly string Executable = Path.Combine(Resources, "csharpfar");

    [Fact]
    public void FindsBundleUsingExecutableNotApplicationsPath()
    {
        Assert.True(MacOsTerminalLauncher.TryGetBundlePaths(Executable, out string exe, out string resources));
        Assert.Equal(Executable, exe);
        Assert.Equal(Resources, resources);
        Assert.False(MacOsTerminalLauncher.TryGetBundlePaths(
            Path.Combine(Path.GetTempPath(), "csharpfar"), out _, out _));
        Assert.Equal(2, new MacOsTerminalLauncher(error: _ => { }).Launch(
            Path.Combine(Path.GetTempPath(), "csharpfar")));
    }

    [Theory]
    [InlineData("auto", true, "Kitty")]
    [InlineData("kitty", true, "Kitty")]
    [InlineData("terminal", true, "Terminal")]
    [InlineData("auto", false, "Terminal")]
    [InlineData("kitty", false, "Terminal")]
    [InlineData("invalid", true, "Kitty")]
    public void ProviderSelectionIsDeterministic(string preference, bool kittyPresent, string expected)
    {
        var invoked = new List<ProcessStartInfo>();
        var launcher = CreateLauncher(kittyPresent, process =>
        {
            invoked.Add(process);
            return true;
        });
        Assert.Equal(0, launcher.LaunchWithPreference(preference, Executable, Resources, Home));
        ProcessStartInfo only = Assert.Single(invoked);
        Assert.False(only.UseShellExecute);
        Assert.Equal(Home, only.WorkingDirectory);
        if (expected == "Kitty")
        {
            Assert.EndsWith("kitty", only.FileName, StringComparison.Ordinal);
            Assert.Equal(["--directory", Home, "--session", "none", Executable], only.ArgumentList);
        }
        else
        {
            Assert.Equal("/usr/bin/open", only.FileName);
            Assert.Equal(["-a", "Terminal", Path.Combine(Resources, "launch.command")],
                only.ArgumentList);
        }
    }

    [Fact]
    public void InvalidBundleIdOrNonExecutableKittyFallsBack()
    {
        foreach (string? identifier in new[] { "other.kitty.app", "net.kovidgoyal.kitty" })
        {
            var seen = new List<ProcessStartInfo>();
            var launcher = CreateLauncher(true, info => { seen.Add(info); return true; },
                bundleIdentifier: identifier,
                canExecute: identifier != "net.kovidgoyal.kitty");
            Assert.Equal(0, launcher.LaunchWithPreference("kitty", Executable, Resources, Home));
            Assert.Equal("/usr/bin/open", Assert.Single(seen).FileName);
        }
    }

    [Fact]
    public void SecondStandardLocationIsUsedWhenFirstIsAbsent()
    {
        var seen = new List<ProcessStartInfo>();
        var launcher = new MacOsTerminalLauncher(
            fileExists: path => path.StartsWith(Path.Combine(Home, "Applications"), StringComparison.Ordinal) &&
                (path.EndsWith("Info.plist", StringComparison.Ordinal) ||
                 Path.GetFileName(path) == "kitty"),
            executable: _ => true,
            bundleIdentifier: _ => "net.kovidgoyal.kitty",
            start: info => { seen.Add(info); return true; },
            error: _ => { });
        Assert.Equal(0, launcher.LaunchWithPreference("auto", Executable, Resources, Home));
        Assert.StartsWith(Path.Combine(Home, "Applications"), Assert.Single(seen).FileName);
    }

    [Fact]
    public void FailedKittyRequestFallsBackExactlyOnce()
    {
        var seen = new List<ProcessStartInfo>();
        var launcher = CreateLauncher(true, info =>
        {
            seen.Add(info);
            return info.FileName == "/usr/bin/open";
        });
        Assert.Equal(0, launcher.LaunchWithPreference("kitty", Executable, Resources, Home));
        Assert.Equal(2, seen.Count);
        Assert.EndsWith("kitty", seen[0].FileName, StringComparison.Ordinal);
        Assert.Equal("/usr/bin/open", seen[1].FileName);
    }

    [Fact]
    public void ExceptionFromKittyStartFallsBack()
    {
        int calls = 0;
        var launcher = CreateLauncher(true, info =>
        {
            calls++;
            if (info.FileName != "/usr/bin/open")
                throw new InvalidOperationException("Kitty could not start");
            return true;
        });
        Assert.Equal(0, launcher.LaunchWithPreference("auto", Executable, Resources, Home));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void AcceptedKittyStartNeverOpensSecondProvider()
    {
        int calls = 0;
        var launcher = CreateLauncher(true, _ => { calls++; return true; });
        Assert.Equal(0, launcher.LaunchWithPreference("kitty", Executable, Resources, Home));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FailedTerminalReturnsNonZeroAndDoesNotLoop()
    {
        int calls = 0;
        var launcher = CreateLauncher(false, _ => { calls++; return false; });
        Assert.Equal(1, launcher.LaunchWithPreference("auto", Executable, Resources, Home));
        Assert.Equal(1, calls);
    }

    private static MacOsTerminalLauncher CreateLauncher(
        bool kittyPresent,
        Func<ProcessStartInfo, bool> launch,
        string? bundleIdentifier = "net.kovidgoyal.kitty",
        bool canExecute = true) =>
        new(
            fileExists: path => kittyPresent && (path.EndsWith("Info.plist", StringComparison.Ordinal) ||
                Path.GetFileName(path) == "kitty"),
            executable: _ => canExecute,
            bundleIdentifier: _ => bundleIdentifier,
            start: launch,
            homeDirectory: () => Home,
            error: _ => { });
}
