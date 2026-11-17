using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

/// <summary>Optional background view preparation; does not mutate panel UI state.</summary>
public interface IAsyncPanelViewBuilder : IPanelViewBuilder
{
    Task<PanelView> BuildAsync(PanelViewRequest request, CancellationToken cancellationToken = default);
}
