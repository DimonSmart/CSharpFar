using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Viewer;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class ViewFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.View;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.OpenRead);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
            return ApplicationCommandResult.Rendered();

        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is null || item.IsParentDirectory || item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        context.History.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileViewer(context.Screen, context.Palette).Show(item.FullPath);
        return ApplicationCommandResult.Rendered();
    }
}
