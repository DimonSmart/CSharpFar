using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.Module.Archives;

/// <summary>Built-in file-open extension, with no archive-specific UI or keyboard routes.</summary>
public sealed class ArchivePlugin : ICSharpFarPlugin
{
    public const string Id = "archives";

    public ValueTask InitializeAsync(IPluginHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host.RegisterFileOpenHandler(new ArchiveFileOpenHandler());
        return ValueTask.CompletedTask;
    }
}

public sealed class ArchiveFileOpenHandler : IFileOpenHandler
{
    public string Id => "open-archive";

    public bool CanOpen(FilePanelItem item) =>
        !item.IsDirectory && ArchiveFilePanelSource.IsSupportedName(item.Name);

    public async ValueTask<PluginFileOpenResult> OpenAsync(
        FileOpenContext context, CancellationToken cancellationToken)
    {
        try
        {
            var source = await ArchiveFilePanelSource.CreateAsync(
                context.Source, context.Location, context.Item.Name, cancellationToken)
                .ConfigureAwait(false);

            return PluginFileOpenResult.OpenPanel(new PluginPanelOpenRequest
            {
                Source = source,
                InitialDirectory = "/",
                Title = $"{source.Format}: {context.Item.Name}",
                Format = source.Format,
            });
        }
        catch (OperationCanceledException)
        {
            return PluginFileOpenResult.Cancelled();
        }
        catch (Exception error)
        {
            return PluginFileOpenResult.Failed($"Cannot open archive '{context.Item.Name}': {error.Message}");
        }
    }
}
