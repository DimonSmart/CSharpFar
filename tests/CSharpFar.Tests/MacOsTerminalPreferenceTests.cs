using System.Text.Json;
using CSharpFar.App.Settings;
using CSharpFar.Core.Models;

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
    public void PortableConfigurationKeepsLegacySettings()
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
