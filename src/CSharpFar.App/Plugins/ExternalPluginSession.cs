namespace CSharpFar.App.Plugins;

internal sealed class ExternalPluginSession
{
    public ExternalPluginManager? Manager { get; private set; }

    public void MarkReady(ExternalPluginManager manager) => Manager = manager;
}
