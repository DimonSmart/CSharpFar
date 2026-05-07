using CSharpFar.Core.Models;

namespace CSharpFar.Core.Highlighting;

public interface IFileHighlightService
{
    HighlightResult GetHighlight(FilePanelItem item, FileRowState state);
}
