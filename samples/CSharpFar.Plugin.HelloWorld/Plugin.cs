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
        host.RegisterCommand(new PluginCommandDescriptor
        {
            Id = "form",
            Title = "Form example",
            ExecuteAsync = (context, token) =>
            {
                if (!context.Ui.SupportsForms)
                    return ValueTask.FromResult(PluginCommandResult.Completed());
                var name = new PluginFormText("name", "Name:");
                var enabled = new PluginFormCheckBox("enabled", "Enable greeting", true);
                var form = new PluginForm("Hello form", [name, enabled])
                {
                    OnEvent = e => e.Kind == PluginFormEventKind.Submit && enabled.Value &&
                        string.IsNullOrWhiteSpace(name.Value)
                        ? new PluginFormFeedback("Enter a name.", name.Id) : null
                };
                if (context.Ui.ShowForm(form, token) is null)
                    return ValueTask.FromResult(PluginCommandResult.Cancelled());
                if (enabled.Value)
                    context.Ui.ShowMessage("Hello form", "Hello " + name.Value);
                return ValueTask.FromResult(PluginCommandResult.Completed());
            }
        });
        host.RegisterCommandPrefix("helloform", "form");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_settingsDirectory is not null)
            File.WriteAllText(Path.Combine(_settingsDirectory, "disposed.marker"), "closed");
        return ValueTask.CompletedTask;
    }
}
