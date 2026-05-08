using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IPanelViewBuilder
{
    PanelView Build(PanelViewRequest request);
}
