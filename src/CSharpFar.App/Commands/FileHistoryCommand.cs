using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.Panels;
using CSharpFar.App.Files;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal sealed class FileHistoryCommand(PanelCommandTargetResolver targetResolver, PanelController controller, PanelFileViewerService viewer, PanelRefreshService panelRefresh, DialogService dialogs, IHistoryStore history, FilePanelSourceRegistry sourceRegistry, Action resetFunctionKeyLayer) : IApplicationCommand
{
    public string CommandId => FunctionKeyCommandIds.FileHistory;

    public bool CanExecute(object? args = null) => true;

    public ApplicationCommandResult Execute(object? args = null)
    {
        var target = targetResolver.ResolvePanelTarget(args);
        if (!PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, target.State))
            return ApplicationCommandResult.Rendered();

        try
        {
            var result = dialogs.Select(new SelectionDialogOptions<FileHistoryItem>
            {
                Title = "File History",
                Items = history.GetFileHistory().Reverse().ToArray(),
                ItemText = static item => item.Path,
                Presentation = SelectionDialogPresentation.Standard,
            });
            string? path = result.IsConfirmed ? result.SelectedItem?.Path : null;
            if (path is null)
                return ApplicationCommandResult.Rendered();

            if (!File.Exists(path))
            {
                dialogs.Message("File History", $"File not found: {path}");
                return ApplicationCommandResult.Rendered();
            }

            var choice = new OpenFileDialog(dialogs).Show(Path.GetFileName(path));
            switch (choice)
            {
                case OpenFileChoice.View:
                    history.AddFile(new FileHistoryItem { Path = path });
                    viewer.ViewFile(path);
                    break;
                case OpenFileChoice.Edit:
                    history.AddFile(new FileHistoryItem { Path = path });
                    viewer.EditFile(path, PanelCommandEditorContextFactory.Create(controller, target));
                    panelRefresh.SafeRefresh(target.State, target.VisibleRows);
                    break;
            }

            return ApplicationCommandResult.Rendered();
        }
        finally
        {
            resetFunctionKeyLayer();
        }
    }

}
