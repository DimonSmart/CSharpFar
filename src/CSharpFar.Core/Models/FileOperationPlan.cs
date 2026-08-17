namespace CSharpFar.Core.Models;

public sealed record FileOperationPlan(IReadOnlyList<FileOperationPlanItem> Items);

public sealed record FileOperationPlanItem(
    PanelLocation Source,
    PanelLocation Destination,
    bool IsDirectory);
