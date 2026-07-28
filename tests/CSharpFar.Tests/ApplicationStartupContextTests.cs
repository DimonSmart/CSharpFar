using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class ApplicationStartupContextTests : IDisposable
{
    private readonly string _root;

    public ApplicationStartupContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CSharpFar.StartupContext." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DemoCreate_DoesNotCallNormalSettingsFactoryOrValidation()
    {
        string normalConfig = Path.Combine(_root, "normal");
        Directory.CreateDirectory(normalConfig);
        string normalSettingsPath = Path.Combine(normalConfig, "settings.json");
        byte[] expected = "original-settings"u8.ToArray();
        File.WriteAllBytes(normalSettingsPath, expected);
        bool normalFactoryCalled = false;
        bool validated = false;

        using var context = ApplicationStartupContext.Create(
            new ApplicationRunOptions(ApplicationRunMode.Demo, Path.Combine(_root, "fixture")),
            () => new AppSettings(),
            () =>
            {
                normalFactoryCalled = true;
                File.WriteAllText(normalSettingsPath, "changed");
                return new JsonSettingsStore(normalConfig, () => new AppSettings());
            },
            _ => validated = true);

        Assert.False(normalFactoryCalled);
        Assert.False(validated);
        Assert.Equal(expected, File.ReadAllBytes(normalSettingsPath));
        Assert.NotEqual(normalConfig, context.SettingsStore.ConfigDirectory);
    }

    [Fact]
    public void DemoCreate_DoesNotCreateAbsentNormalSettingsFile()
    {
        string normalConfig = Path.Combine(_root, "normal-absent");
        string normalSettingsPath = Path.Combine(normalConfig, "settings.json");
        bool normalFactoryCalled = false;

        using var _ = ApplicationStartupContext.Create(
            new ApplicationRunOptions(ApplicationRunMode.Demo, Path.Combine(_root, "fixture")),
            () => new AppSettings(),
            () =>
            {
                normalFactoryCalled = true;
                Directory.CreateDirectory(normalConfig);
                return new JsonSettingsStore(normalConfig, () => new AppSettings());
            },
            _ => { });

        Assert.False(normalFactoryCalled);
        Assert.False(File.Exists(normalSettingsPath));
    }
}
