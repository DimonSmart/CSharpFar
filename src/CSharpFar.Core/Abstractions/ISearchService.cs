using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface ISearchService
{
    IAsyncEnumerable<SearchResultItem> SearchAsync(
        SearchRequest request,
        IProgress<SearchProgress>? progress,
        CancellationToken cancellationToken = default);
}
