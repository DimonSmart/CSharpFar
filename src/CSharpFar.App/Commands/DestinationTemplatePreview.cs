using CSharpFar.App.Dialogs;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal static class DestinationTemplatePreview
{
    public static void Show(IFileOperationService fileOperations, DialogService dialogs, FileOperationRequest request)
    {
        if (fileOperations is not IFileOperationPlanBuilder planner)
            throw new InvalidOperationException("Destination template preview is not available for the active file-operation service.");

        var plan = dialogs.Operation<string, FileOperationPlan, FileOperationPlan?>(
            new OperationDialogDefinition<string, FileOperationPlan, FileOperationPlan?>
            {
                Title = "Preparing destination preview",
                PreferredWidth = 54,
                PreferredHeight = 8,
                MinWidth = 30,
                MinHeight = 8,
                Operation = ct => planner.BuildPlanAsync(request, ct),
                Synchronize = () => new OperationDialogState<string>(status: "Preparing preview... (Esc to cancel)"),
                HandleCancel = () => OperationDialogOutcome<FileOperationPlan?>.Complete(null),
                HandleOperationCompleted = result => OperationDialogOutcome<FileOperationPlan?>.Complete(result),
                Complete = result => result,
            });
        if (plan is not null)
            new DestinationTemplatePreviewDialog(dialogs).Show(plan);
    }
}
