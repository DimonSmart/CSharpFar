using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Ui;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.UserMenu;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class SelectLeftDriveCommand : DriveSelectionCommand
{
    public SelectLeftDriveCommand(PanelWorkspaceController workspace, PanelSessionState panels, ApplicationState appState, IVolumeService? volumeService, NativeModuleCatalog moduleCatalog, ModulePanelOpener modulePanelOpener, ModalDialogHost modalDialogs, DialogService dialogs, PanelNavigationService navigation, PanelAutoRefreshService autoRefresh, IHistoryStore history, Action resetFunctionKeyLayer)
        : base(FunctionKeyCommandIds.LeftVolume, PanelSide.Left, workspace, panels, appState, volumeService, moduleCatalog, modulePanelOpener, modalDialogs, dialogs, navigation, autoRefresh, history, resetFunctionKeyLayer)
    {
    }
}
