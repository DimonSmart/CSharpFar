using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class EditFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Edit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ActiveState, PanelProviderCapabilities.Edit);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Edit");
            return ApplicationCommandResult.Rendered();
        }

        var item = context.Controller.CurrentItem(context.ActiveState);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        if (context.TryEditFarNetPanelItem(context.ActiveState, item))
            return ApplicationCommandResult.Rendered();

        if (item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        context.History.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileEditor(
            context.Screen,
            context.Palette,
            context.Settings.Editor,
            context.TextClipboard,
            BuildFileNameInsertionContext(context, item)).Show(item.FullPath);
        context.SafeRefresh(context.ActiveState, context.VisibleRows());
        return ApplicationCommandResult.Rendered();
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(
        ApplicationCommandContext context,
        FilePanelItem activeItem)
    {
        var passiveItem = context.Controller.CurrentItem(context.PassiveState);
        return new EditorFileNameInsertionContext(
            activeItem.Name,
            activeItem.FullPath,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
    }
}
