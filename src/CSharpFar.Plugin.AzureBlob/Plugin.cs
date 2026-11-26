using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.AzureBlob;

public sealed class Plugin : ICSharpFarPlugin
{
    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        var provider = new AzureBlobPanelProvider();
        host.RegisterPanelProvider(provider);
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "open", Title = "Azure Blob Storage",
            ExecuteAsync = (context, token) => provider.ExecuteAsync(context.Ui, context.Settings, token)
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "plugins", Title = "Azure Blob Storage...", CommandId = "open", Placement = PluginMenuPlacement.Plugins
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "disk", Title = "Azure Blob Storage", CommandId = "open", Placement = PluginMenuPlacement.Disk
        });
        host.RegisterCommandPrefix("azure", "open");
        return ValueTask.CompletedTask;
    }
}
