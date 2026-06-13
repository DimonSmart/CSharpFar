namespace CSharpFar.Plugin.Abstractions;

public interface IPluginSettingsService
{
    string GetSettingsDirectory(string pluginId);
}
