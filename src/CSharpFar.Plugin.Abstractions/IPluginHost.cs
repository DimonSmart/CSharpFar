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

    // Default implementation preserves binary and source compatibility with existing hosts.
    void RegisterFileOpenHandler(IFileOpenHandler handler) =>
        throw new NotSupportedException("File-open handlers are not supported by this host.");
}
