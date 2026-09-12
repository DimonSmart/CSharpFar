using CSharpFar.Console;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed class DestinationTemplatePreviewDialog
{
    private readonly DialogService _dialogs;

    public DestinationTemplatePreviewDialog(DialogService dialogs) => _dialogs = dialogs;

    public void Show(FileOperationPlan plan)
    {
        _ = _dialogs.Table(new TableDialogOptions<FileOperationPlanItem, bool>
        {
            Title = "Destination template preview",
            Items = () => plan.Items,
            Definition = new TableListDefinition<FileOperationPlanItem>
            {
                Columns =
                [
                    TableColumn<FileOperationPlanItem>.Text("Source", item => item.Source.SourcePath, TableWidth.Flexible(24, 12), emphasized: true),
                    TableColumn<FileOperationPlanItem>.Text("Destination", item => item.Destination.SourcePath, TableWidth.Flexible(36, 16)),
                ],
            },
            Actions = [DialogButton.Default("close", "Close", 'C')],
            DefaultItemActionId = null,
            CancelKeys = [ConsoleKey.Escape],
            Cancel = () => true,
            HandleAction = action => action.ActionId == "close"
                ? DialogOutcome<bool>.Complete(true)
                : DialogOutcome<bool>.ContinueOpen(),
            FooterText = $"{plan.Items.Count} planned item(s)",
            PreferredWidth = 90,
            PreferredHeight = 22,
            MinWidth = 48,
            MinHeight = 8,
        });
    }
}
