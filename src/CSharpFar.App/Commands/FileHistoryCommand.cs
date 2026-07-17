using CSharpFar.App.Dialogs;
using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Viewer;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class FileHistoryCommand : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.FileHistory;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var target = context.ResolvePanelTarget(args);
        try
        {
            string? path = new FileHistoryDialog(context.ModalDialogs).Show(context.History.GetFileHistory());
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!File.Exists(path))
            {
                new MessageDialog(context.ModalDialogs).Show("File History", $"File not found: {path}");
                return ApplicationCommandResult.Rendered();
            }

            var choice = new OpenFileDialog(context.ModalDialogs).Show(Path.GetFileName(path));
            switch (choice)
            {
                case OpenFileChoice.View:
                    context.History.AddFile(new FileHistoryItem { Path = path });
                    new FileViewer(context.Screen, context.ModalDialogs, context.Palette).Show(path);
                    break;
                case OpenFileChoice.Edit:
                    context.History.AddFile(new FileHistoryItem { Path = path });
                    new FileEditor(
                        context.Screen,
                        context.ModalDialogs,
                        context.Palette,
                        context.Settings.Editor,
                        context.TextClipboard,
                        BuildFileNameInsertionContext(context, target)).Show(path);
                    context.SafeRefresh(target.State, target.VisibleRows);
                    break;
            }

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(
        ApplicationCommandContext context,
        ResolvedPanelCommandTarget target)
    {
        FilePanelItem? activeItem = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.State, target.ActiveCommitted, context.Controller, out var resolvedActive) ? resolvedActive : null;
        FilePanelItem? passiveItem = ApplicationCommandContext.TryResolveCommittedCurrentItem(
            target.PassiveState, target.PassiveCommitted, context.Controller, out var resolvedPassive) ? resolvedPassive : null;
        return new EditorFileNameInsertionContext(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
    }
}
