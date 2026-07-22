using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
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
                    context.ViewFile(path);
                    break;
                case OpenFileChoice.Edit:
                    context.History.AddFile(new FileHistoryItem { Path = path });
                    context.EditFile(path, PanelCommandEditorContextFactory.Create(context, target));
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

}
