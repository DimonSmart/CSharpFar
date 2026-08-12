using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.Module.Sftp;

internal enum SftpConnectionManagerAction
{
    Connect,
    Create,
    Edit,
    Delete,
}

internal sealed record SftpConnectionManagerResult(
    SftpConnectionManagerAction Action,
    SftpConnectionInfo? Connection);

internal sealed class SftpConnectionManagerDialog
{
    private readonly DialogService _dialogs;

    public SftpConnectionManagerDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public SftpConnectionManagerResult? Show(IReadOnlyList<SftpConnectionInfo> connections)
    {
        return _dialogs.List(new ListDialogOptions<SftpConnectionInfo, SftpConnectionManagerResult>
        {
            Title = "SFTP connections",
            Items = () => connections,
            ItemText = FormatConnection,
            Actions = CreateButtons(connections.Count > 0),
            DialogWidth = 68,
            MinDialogWidth = 40,
            MaxVisibleRows = 12,
            EmptyText = "No saved SFTP connections.",
            DefaultItemActionId = "connect",
            CancelActionId = "cancel",
            DeleteActionId = "delete",
            HandleAction = action => ToManagerResult(action) is { } result
                ? DialogOutcome<SftpConnectionManagerResult>.Complete(result)
                : DialogOutcome<SftpConnectionManagerResult>.ContinueOpen(),
        });
    }

    private static IReadOnlyList<DialogButton> CreateButtons(bool hasConnections) =>
        hasConnections
            ? [
                DialogButton.Default("connect", "Connect", 'O'),
                DialogButton.Action("create", "New", 'N'),
                DialogButton.Action("edit", "Edit", 'E'),
                DialogButton.Action("delete", "Delete", 'D'),
                DialogButton.Cancel(),
            ]
            : [
                DialogButton.Default("create", "New", 'N'),
                DialogButton.Cancel(),
            ];

    private static SftpConnectionManagerResult? ToManagerResult(ListDialogActionContext<SftpConnectionInfo> result) =>
        result.ActionId switch
        {
            "connect" when result.SelectedItem is not null => new(SftpConnectionManagerAction.Connect, result.SelectedItem),
            "create" => new(SftpConnectionManagerAction.Create, null),
            "edit" when result.SelectedItem is not null => new(SftpConnectionManagerAction.Edit, result.SelectedItem),
            "delete" when result.SelectedItem is not null => new(SftpConnectionManagerAction.Delete, result.SelectedItem),
            _ => null,
        };

    private static string FormatConnection(SftpConnectionInfo connection)
    {
        string marker = connection.ShowInDriveSelection ? "*" : " ";
        return $"{marker} {connection.DisplayName}  {connection.Username}@{connection.Host}:{connection.Port}";
    }
}
