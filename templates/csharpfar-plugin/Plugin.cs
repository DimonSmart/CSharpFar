using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Plugin.Template;

public sealed class Plugin : ICSharpFarPlugin
{
    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "show",
            Title = "Sample Extension",
            ExecuteAsync = (context, token) =>
            {
                token.ThrowIfCancellationRequested();
                context.Ui.ShowMessage("Sample Extension", "The native plugin is installed.");
                return ValueTask.FromResult(PluginCommandResult.Completed());
            }
        });

        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "show-menu",
            Title = "Sample Extension...",
            CommandId = "show",
            Placement = PluginMenuPlacement.Plugins
        });

        host.RegisterCommandPrefix("sample-extension", "show");
        return ValueTask.CompletedTask;
    }
}
