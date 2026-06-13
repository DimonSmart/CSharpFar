namespace CSharpFar.Plugin.Abstractions;

public interface ICSharpFarPlugin
{
    ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken);
}
