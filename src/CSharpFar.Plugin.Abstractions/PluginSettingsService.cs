namespace CSharpFar.Plugin.Abstractions;

public sealed class PluginSettingsService
{
    private readonly string _configDirectory;

    public PluginSettingsService(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
    }

    public string GetSettingsDirectory(Guid pluginId)
    {
        string directory = Path.Combine(_configDirectory, "plugins", pluginId.ToString("D"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
