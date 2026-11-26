using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IPanelViewBuilder
{
    PanelView Build(PanelViewRequest request);

    Task<PanelView> BuildAsync(PanelViewRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Build(request), cancellationToken);
}
