using CSharpFar.Core.Abstractions;

namespace CSharpFar.Plugin.Abstractions;

public interface IPanelProvider
{
    string Id { get; }

    string Title { get; }

    ValueTask<IFilePanelSource> OpenAsync(
        IPanelProviderContext context,
        CancellationToken cancellationToken);
}
