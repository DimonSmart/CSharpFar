using CSharpFar.App.Settings;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Bootstrap;

public sealed class ApplicationStartupContext : IDisposable
{
    private readonly string? _ephemeralConfigDirectory;

    private ApplicationStartupContext(
        ApplicationRunOptions runOptions,
        JsonSettingsStore settingsStore,
        string? ephemeralConfigDirectory)
    {
        RunOptions = runOptions;
        SettingsStore = settingsStore;
        _ephemeralConfigDirectory = ephemeralConfigDirectory;
    }

    public ApplicationRunOptions RunOptions { get; }

    public JsonSettingsStore SettingsStore { get; }

    public static ApplicationStartupContext Create(
        ApplicationRunOptions runOptions,
        Func<AppSettings> createDefaultSettings,
        Action<JsonSettingsStore> validateNormalSettings)
        => Create(
            runOptions,
            createDefaultSettings,
            () => JsonSettingsStore.Create(createDefaultSettings: createDefaultSettings),
            validateNormalSettings);

    internal static ApplicationStartupContext Create(
        ApplicationRunOptions runOptions,
        Func<AppSettings> createDefaultSettings,
        Func<JsonSettingsStore> createNormalSettingsStore,
        Action<JsonSettingsStore> validateNormalSettings)
    {
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(createDefaultSettings);
        ArgumentNullException.ThrowIfNull(createNormalSettingsStore);
        ArgumentNullException.ThrowIfNull(validateNormalSettings);

        if (runOptions.Mode == ApplicationRunMode.Demo)
        {
            string configDirectory = Path.Combine(
                Path.GetTempPath(),
                "CSharpFar.Demo",
                Guid.NewGuid().ToString("N"));
            var settingsStore = new JsonSettingsStore(configDirectory, createDefaultSettings);
            return new ApplicationStartupContext(runOptions, settingsStore, configDirectory);
        }

        var normalStore = createNormalSettingsStore();
        validateNormalSettings(normalStore);
        return new ApplicationStartupContext(runOptions, normalStore, ephemeralConfigDirectory: null);
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_ephemeralConfigDirectory) ||
            !Directory.Exists(_ephemeralConfigDirectory))
            return;

        try
        {
            Directory.Delete(_ephemeralConfigDirectory, recursive: true);
        }
        catch
        {
        }
    }
}
