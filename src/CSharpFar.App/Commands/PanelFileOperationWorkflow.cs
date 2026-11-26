using CSharpFar.Ui;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Files;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Commands;

internal sealed class PanelFileOperationWorkflow(
    FileOperationUiRunner runner,
    IFileOperationService fileOperations,
    DialogService dialogs,
    AppSettingsAlias settings,
    PanelRefreshService refresh,
    PanelSessionState panels)
{
    public bool SupportsRecycleBin => fileOperations.SupportsRecycleBin;

    public FileOperationOptions BuildOptions() => FileOperationOptionsFactory.Create(settings);

    public FileOperationResult Execute(FileOperationRequest request) => runner.Execute(request);

    public void Preview(FileOperationRequest request) =>
        DestinationTemplatePreview.Show(fileOperations, dialogs, request);

    public void RefreshPanels() => refresh.RefreshPanels(panels.Left, panels.Right);

    public void RefreshAfterOperation() => refresh.RefreshPanelsAfterFileOperation(panels.Left, panels.Right);
}
