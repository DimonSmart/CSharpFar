using CSharpFar.Plugin.Abstractions;
using PluginSharedDependency;

namespace GoodbyeWorld;

public sealed class Plugin : ICSharpFarPlugin
{
    public async ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        string settings = host.Settings.GetSettingsDirectory("goodbye-world");
        if (File.Exists(Path.Combine(settings, "fail-init")))
            throw new InvalidOperationException("Intentional external plugin initialization failure.");

        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "goodbye",
            Title = "Goodbye World",
            ExecuteAsync = (context, token) =>
            {
                context.Ui.ShowMessage("Goodbye World", $"Dependency {Marker.Version}");
                return ValueTask.FromResult(PluginCommandResult.Completed());
            },
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "goodbye-menu",
            Title = "Goodbye World...",
            CommandId = "goodbye",
        });
        host.RegisterCommandPrefix("goodbye", "goodbye");
    }
}
