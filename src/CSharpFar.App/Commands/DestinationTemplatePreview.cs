using CSharpFar.App.Dialogs;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal static class DestinationTemplatePreview
{
    public static void Show(ApplicationCommandContext context, FileOperationRequest request)
    {
        if (context.FileOperations is not IFileOperationPlanBuilder planner)
            throw new InvalidOperationException("Destination template preview is not available for the active file-operation service.");

        var plan = context.Dialogs.Operation<string, FileOperationPlan, FileOperationPlan?>(
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
            new DestinationTemplatePreviewDialog(context.Dialogs).Show(plan);
    }
}
