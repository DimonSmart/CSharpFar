namespace CSharpFar.Plugin.Abstractions;

public interface IPluginHost
{
    IPluginApplicationContext Application { get; }
    IPluginSettingsService Settings { get; }
    IPluginUiService Ui { get; }
    IPluginCredentialService Credentials { get; }

    void RegisterCommand(PluginCommandDescriptor command);
    void RegisterMenuItem(PluginMenuItemDescriptor menuItem);
    void RegisterCommandPrefix(string prefix, string commandId);
    void RegisterPanelProvider(IPanelProvider panelProvider);
}
