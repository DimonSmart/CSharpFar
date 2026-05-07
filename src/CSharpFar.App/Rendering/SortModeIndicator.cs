using CSharpFar.Core.Models;

namespace CSharpFar.App.Rendering;

internal static class SortModeIndicator
{
    public static char For(FilePanelState state)
    {
        char indicator = state.SortMode switch
        {
            SortMode.Name          => 'n',
            SortMode.Extension     => 'e',
            SortMode.LastWriteTime => 'w',
            SortMode.Size          => 's',
            _                      => 'n',
        };

        return state.SortDescending ? char.ToUpperInvariant(indicator) : indicator;
    }
}
