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
        try
        {
            string? path = new FileHistoryDialog(context.Screen).Show(context.History.GetFileHistory());
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!File.Exists(path))
            {
                new MessageDialog(context.ModalDialogs).Show("File History", $"File not found: {path}");
                return ApplicationCommandResult.Rendered();
            }

            var choice = new OpenFileDialog(context.Screen).Show(Path.GetFileName(path));
            switch (choice)
            {
                case OpenFileChoice.View:
                    context.History.AddFile(new FileHistoryItem { Path = path });
                    new FileViewer(context.Screen, context.Palette).Show(path);
                    break;
                case OpenFileChoice.Edit:
                    context.History.AddFile(new FileHistoryItem { Path = path });
                    new FileEditor(
                        context.Screen,
                        context.Palette,
                        context.Settings.Editor,
                        context.TextClipboard,
                        BuildFileNameInsertionContext(context)).Show(path);
                    context.SafeRefresh(context.ActiveState, context.VisibleRows());
                    break;
            }

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }

    private static EditorFileNameInsertionContext BuildFileNameInsertionContext(ApplicationCommandContext context)
    {
        var activeItem = context.Controller.CurrentItem(context.ActiveState);
        var passiveItem = context.Controller.CurrentItem(context.PassiveState);
        return new EditorFileNameInsertionContext(
            activeItem is { IsParentDirectory: false } ? activeItem.Name : null,
            activeItem is { IsParentDirectory: false } ? activeItem.FullPath : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.Name : null,
            passiveItem is { IsParentDirectory: false } ? passiveItem.FullPath : null);
    }
}
