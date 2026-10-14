namespace CSharpFar.Plugin.Abstractions;

public interface IPanelProvider
{
    string Id { get; }
    string Title { get; }
    ValueTask<PluginPanelOpenRequest> OpenAsync(
        IPanelProviderContext context,
        CancellationToken cancellationToken);
}
