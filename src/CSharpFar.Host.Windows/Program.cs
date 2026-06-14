using CSharpFar.App.Bootstrap;
using CSharpFar.App.Settings;
using CSharpFar.Platform.Windows;

var settingsStore = JsonSettingsStore.Create(
    createDefaultSettings: WindowsPlatformServices.CreateDefaultSettings);

using var platform = WindowsPlatformServices.Create(
    settingsStore.ConfigDirectory,
    settingsStore.Settings.Shell);

ApplicationBootstrap.Run(platform.ConsoleDriver, platform, settingsStore);
