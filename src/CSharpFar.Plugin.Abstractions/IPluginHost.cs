namespace CSharpFar.Plugin.Abstractions;

public interface IPluginHost
{
    IPluginApplicationContext Application { get; }

    IPluginSettingsService Settings { get; }

    IPluginUiService Ui { get; }

    void RegisterCommand(PluginCommandDescriptor command);

    void RegisterMenuItem(PluginMenuItemDescriptor menuItem);

    void RegisterPanelProvider(IPanelProvider panelProvider);
}
