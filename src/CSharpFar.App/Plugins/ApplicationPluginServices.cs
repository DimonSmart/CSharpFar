using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal sealed class ApplicationPluginContext(
    PluginRunMode runMode, Func<PanelSide> activeSide, Func<string> currentDirectory)
    : IPluginApplicationContext
{
    public PluginRunMode RunMode => runMode;
    public PanelSide ActivePanelSide => activeSide();
    public string CurrentDirectory => currentDirectory();
}

internal sealed class ApplicationPluginUi(ModuleUiServices ui) : IPluginUiService
{
    public void ShowMessage(string title, string message) => ui.ShowMessage(title, message);
    public string? Input(string title, string prompt, string? initialText = null) =>
        ui.Input(title, prompt, initialText);
    public int? ShowMenu(string title, IReadOnlyList<string> items, int selected) =>
        ui.ShowMenu(title, items, selected);
}

internal sealed class ApplicationPluginSettings(string configDirectory) : IPluginSettingsService
{
    public string GetSettingsDirectory(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (!pluginId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            || pluginId is "." or "..")
            throw new ArgumentException("Invalid plugin ID.", nameof(pluginId));

        string path = Path.Combine(configDirectory, "plugins", pluginId);
        Directory.CreateDirectory(path);
        return path;
    }
}

internal sealed class ApplicationPluginCredentials(
    ICredentialStore? store, string pluginId) : IPluginCredentialService
{
    private string Key(string credentialId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialId);
        return pluginId + ":" + credentialId;
    }

    public string? TryReadPassword(string credentialId) =>
        (store ?? throw Unavailable()).TryReadPassword(Key(credentialId));

    public void SavePassword(string credentialId, string password) =>
        (store ?? throw Unavailable()).SavePassword(Key(credentialId), password);

    public void DeletePassword(string credentialId) =>
        (store ?? throw Unavailable()).DeletePassword(Key(credentialId));

    private static InvalidOperationException Unavailable() =>
        new("Credential storage is not configured.");
}
