namespace CSharpFar.Module.Abstractions;

public sealed class ModuleSettingsService
{
    private readonly string _configDirectory;

    public ModuleSettingsService(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);
        _configDirectory = configDirectory;
    }

    public string GetSettingsDirectory(Guid moduleId)
    {
        string directory = Path.Combine(_configDirectory, "plugins", moduleId.ToString("D"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
