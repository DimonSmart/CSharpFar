using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class EditFileCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.Edit;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.HasCapability(context.ResolvePanelTarget(args).State, PanelProviderCapabilities.Edit);

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        if (!CanExecute(context, args))
        {
            context.ShowReadOnlyPanelMessage("Edit");
            return ApplicationCommandResult.Rendered();
        }

        FilePanelItem? item = target.Committed is { } committed
            ? ApplicationCommandContext.TryResolveCommittedCurrentItem(target.State, committed, out var committedItem)
                ? committedItem
                : null
            : context.Controller.CurrentItem(target.State);
        if (item is null || item.IsParentDirectory)
            return ApplicationCommandResult.Rendered();

        if (item.IsDirectory)
            return ApplicationCommandResult.Rendered();

        context.History.AddFile(new FileHistoryItem { Path = item.FullPath });
        new FileEditor(
            context.Screen,
            context.ModalDialogs,
            context.Palette,
            context.Settings.Editor,
            context.TextClipboard,
            BuildFileNameInsertionContext(context, target, item)).Show(item.FullPath);
        context.SafeRefresh(target.State, target.VisibleRows);
        return ApplicationCommandResult.Rendered();
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target,
        FilePanelItem activeItem)
    {
        FilePanelItem? passiveItem = null;
        if (target.PassiveState == context.LeftPanel || target.PassiveState == context.RightPanel)
            passiveItem = context.Controller.CurrentItem(target.PassiveState);
        return new EditorFileNameInsertionContext(
            activeItem.Name,
            activeItem.FullPath,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
    }
}
