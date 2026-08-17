using CSharpFar.App.Dialogs;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class DestinationTemplatePreview
{
    public static void Show(ApplicationCommandContext context, FileOperationRequest request)
    {
        if (context.FileOperations is not IFileOperationPlanBuilder planner)
            throw new InvalidOperationException("Destination template preview is not available for the active file-operation service.");

        new DestinationTemplatePreviewDialog(context.Dialogs).Show(planner.BuildPlan(request));
    }
}
