using CSharpFar.Plugin.Abstractions;
using PluginSharedDependency;

namespace HelloWorld;

public sealed class Plugin : ICSharpFarPlugin, IAsyncDisposable
{
    private string? _settingsDirectory;

    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _settingsDirectory = host.Settings.GetSettingsDirectory("hello-world");
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "greet",
            Title = "Hello World",
            ExecuteAsync = (context, token) =>
            {
                token.ThrowIfCancellationRequested();
                string valueFile = Path.Combine(_settingsDirectory!, "counter.txt");
                int count = File.Exists(valueFile) &&
                    int.TryParse(File.ReadAllText(valueFile), out int previous) ? previous : 0;
                File.WriteAllText(valueFile, (count + 1).ToString());
                context.Ui.ShowMessage("Hello World", $"Greetings from an external plugin. Run #{count + 1}. Dependency {Marker.Version}.");
                return ValueTask.FromResult(PluginCommandResult.Completed());
            },
        });
        host.RegisterMenuItem(new PluginMenuItemDescriptor
        {
            Id = "hello-menu",
            Title = "Hello World...",
            CommandId = "greet",
            Placement = PluginMenuPlacement.Plugins,
        });
        host.RegisterCommandPrefix("hello", "greet");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_settingsDirectory is not null)
            File.WriteAllText(Path.Combine(_settingsDirectory, "disposed.marker"), "closed");
        return ValueTask.CompletedTask;
    }
}
