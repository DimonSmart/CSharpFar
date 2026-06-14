using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.Platform.Unix;

var settingsStore = JsonSettingsStore.Create(
    createDefaultSettings: UnixPlatformServices.CreateDefaultSettings);

using var platform = UnixPlatformServices.Create(
    settingsStore.ConfigDirectory,
    settingsStore.Settings.Shell);

ApplicationBootstrap.Run(platform.ConsoleDriver, platform, settingsStore);
