using CSharpFar.Core.Highlighting;

namespace CSharpFar.App.Highlighting;

internal sealed class FileHighlightState(IFileHighlightService? initial)
{
    public IFileHighlightService? Current { get; set; } = initial;
}
