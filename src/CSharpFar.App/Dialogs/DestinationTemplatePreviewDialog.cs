using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DestinationTemplatePreviewDialog
{
    private readonly DialogService _dialogs;

    public DestinationTemplatePreviewDialog(DialogService dialogs) => _dialogs = dialogs;

    public void Show(FileOperationPlan plan)
    {
        var table = new TableList<FileOperationPlanItem>(plan.Items, new TableListDefinition<FileOperationPlanItem>
        {
            Columns =
            [
                TableColumn<FileOperationPlanItem>.Text("Source", item => item.Source.SourcePath, TableWidth.Flexible(24, 12), emphasized: true),
                TableColumn<FileOperationPlanItem>.Text("Destination", item => item.Destination.SourcePath, TableWidth.Flexible(36, 16)),
            ],
        });
        var close = FormControls.Buttons(DialogButton.Default("close", "Close", 'C'));
        var form = new ScrollableFormDialog();
        form.SetRows([], [close]);
        _dialogs.Composite(
            new CompositeDialogOptions("Destination template preview", 90, 22, 48, 8),
            form,
            table,
            () => $"{plan.Items.Count} planned item(s)",
            new Dictionary<ConsoleKey, string> { [ConsoleKey.Escape] = "close" },
            semantic => semantic.Kind is CompositeDialogEventKind.Cancelled || semantic is { Kind: CompositeDialogEventKind.Command, Command: "close" }
                ? CompositeDialogOutcome<bool>.Complete(true)
                : CompositeDialogOutcome<bool>.ContinueNoChange);
    }
}
