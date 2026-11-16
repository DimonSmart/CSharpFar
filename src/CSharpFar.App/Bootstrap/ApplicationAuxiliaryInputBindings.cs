using CSharpFar.App.Input;
using CSharpFar.App.Panels;
using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Bootstrap;

// Connect optional quick-view and file-usage actions after their controllers exist.
internal static class ApplicationAuxiliaryInputBindings
{
    public static void Bind(
        KeyboardInputContext keyboardInputContext,
        MouseInputContext mouseInputContext,
        ApplicationSession session,
        PanelController controller,
        ApplicationServiceCallbacks callbacks,
        PanelWorkspaceController panelWorkspace,
        QuickViewDirectorySizeController quickViewDirectorySize,
        FileUsagePanelController fileUsagePanel,
        ModalDialogHost modalDialogs)
    {
        keyboardInputContext.ToggleFileUsage = () =>
        {
            if (session.App.FileUsage)
            {
                session.App.FileUsage = false;
                session.App.QuickView = session.App.RestoreQuickViewAfterFileUsage;
                fileUsagePanel.Update(false, PanelSourceId.Local, null);
            }
            else
            {
                session.App.RestoreQuickViewAfterFileUsage = session.App.QuickView;
                session.App.QuickView = false;
                session.App.FileUsage = true;
            }
            return true;
        };
        keyboardInputContext.MoveFileUsageOwnerSelection = offset =>
            session.App.FileUsage && fileUsagePanel.MoveSelection(offset);
        keyboardInputContext.UnlockFileUsageOwner = () =>
            session.App.FileUsage && fileUsagePanel.RequestUnlock(message =>
                new MessageDialog(modalDialogs).ShowButtons("Unlock owner", message, ["Unlock", "Cancel"]) == 0);
        mouseInputContext.SelectFileUsageOwner = index =>
            session.App.FileUsage && fileUsagePanel.SelectOwner(index);
        keyboardInputContext.ToggleQuickViewDirectoryMonitor = () =>
        {
            if (!session.App.QuickView)
                return false;
            quickViewDirectorySize.ToggleMonitor();
            return true;
        };
        keyboardInputContext.ActivateQuickViewDirectoryMonitorChange = () =>
        {
            if (!session.App.QuickView || !quickViewDirectorySize.TryGetSelectedMonitorTarget(out string target))
                return false;

            PanelSide targetSide = panelWorkspace.ActiveSide;
            FilePanelState targetPanel = targetSide == PanelSide.Left ? session.Panels.Left : session.Panels.Right;
            if (targetPanel.SourceId != PanelSourceId.Local || targetPanel.ContentKind != PanelContentKind.Source)
                return false;

            string? parent = Path.GetDirectoryName(target);
            if (parent is null || !controller.TryLoadDirectory(targetPanel, parent, callbacks.PanelOptions()))
                return false;
            controller.SetCursorByName(targetPanel, Path.GetFileName(target), callbacks.VisibleRowsForSide(targetSide));
            callbacks.StartWatching(targetPanel, targetSide);
            return true;
        };
        mouseInputContext.ActivateQuickViewDirectoryMonitorChange = changeId =>
        {
            if (!session.App.QuickView || !quickViewDirectorySize.SelectMonitorChange(changeId))
                return false;
            return keyboardInputContext.ActivateQuickViewDirectoryMonitorChange();
        };
        mouseInputContext.ToggleQuickViewDirectoryMonitor = keyboardInputContext.ToggleQuickViewDirectoryMonitor;
        mouseInputContext.HandleQuickViewRecentChangesInput = confirmed =>
        {
            if (!session.App.QuickView) return false;
            quickViewDirectorySize.SynchronizeRecentChangesSelection();
            return !confirmed || keyboardInputContext.ActivateQuickViewDirectoryMonitorChange();
        };
        keyboardInputContext.MoveQuickViewDirectoryMonitorSelection = offset =>
            session.App.QuickView && quickViewDirectorySize.MoveMonitorSelection(offset);
        keyboardInputContext.MoveQuickViewDirectoryMonitorSelectionByPage = direction =>
            session.App.QuickView && quickViewDirectorySize.MoveMonitorSelectionByPage(direction);
    }
}
