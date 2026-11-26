using CSharpFar.Ui;
using CSharpFar.App.Files;
using CSharpFar.App.Modules;
using CSharpFar.App.Panels;
using CSharpFar.Core.Controllers;
namespace CSharpFar.App.Commands;

internal sealed class OpenCurrentItemCommand(PanelController controller, PanelWorkspaceController workspace,
    PanelSearchResultsService searchResults, PanelNavigationService navigation,
    ModulePanelOpener modulePanelOpener, PanelFileOpener fileOpener) : IApplicationCommand
{
    public string CommandId => ApplicationCommandIds.OpenCurrentItem;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        var item = controller.CurrentItem(workspace.ActiveState);
        if (item is not null)
        {
            var state = workspace.ActiveState;
            var side = workspace.ActiveSide;
            if (state.SearchRequest is not null)
                searchResults.GoToResult(state, side, item);
            else if (item.IsDirectory)
                navigation.OpenDirectoryItem(state, side, item);
            else if (!modulePanelOpener.TryOpenFileItem(state, side, item, fileOpener.OpenFileItem))
                fileOpener.OpenFileItem(state, item);
        }

        return ApplicationCommandResult.Rendered();
    }
}
