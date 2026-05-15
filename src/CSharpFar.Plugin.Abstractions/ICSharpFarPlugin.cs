namespace CSharpFar.Plugin.Abstractions;

public interface ICSharpFarPlugin
{
    PluginGlobalInfo GetGlobalInfo();

    PluginInfo GetPluginInfo();

    void SetStartupInfo(PluginStartupInfo startupInfo);

    PluginOpenResult Open(PluginOpenInfo openInfo);
}
