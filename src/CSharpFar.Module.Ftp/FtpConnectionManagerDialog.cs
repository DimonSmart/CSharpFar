using CSharpFar.Console;
using CSharpFar.Ui;

namespace CSharpFar.Module.Ftp;

internal enum FtpConnectionManagerAction
{
    Connect,
    Create,
    Edit,
    Delete,
}

internal sealed record FtpConnectionManagerResult(
    FtpConnectionManagerAction Action,
    FtpConnectionInfo? Connection);

internal sealed class FtpConnectionManagerDialog
{
    private readonly DialogService _dialogs;

    public FtpConnectionManagerDialog(DialogService dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public FtpConnectionManagerResult? Show(IReadOnlyList<FtpConnectionInfo> connections)
    {
        return _dialogs.List(new ListDialogOptions<FtpConnectionInfo, FtpConnectionManagerResult>
        {
            Title = "FTP/FTPS connections",
            Items = () => connections,
            ItemText = FormatConnection,
            Actions = CreateButtons(connections.Count > 0),
            DialogWidth = 76,
            MinDialogWidth = 44,
            MaxVisibleRows = 12,
            EmptyText = "No saved FTP/FTPS connections.",
            DefaultItemActionId = "connect",
            CancelActionId = "cancel",
            DeleteActionId = "delete",
            HandleAction = action => ToManagerResult(action) is { } result
                ? DialogOutcome<FtpConnectionManagerResult>.Complete(result)
                : DialogOutcome<FtpConnectionManagerResult>.ContinueOpen(),
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

    private static FtpConnectionManagerResult? ToManagerResult(ListDialogActionContext<FtpConnectionInfo> result) =>
        result.ActionId switch
        {
            "connect" when result.SelectedItem is not null => new(FtpConnectionManagerAction.Connect, result.SelectedItem),
            "create" => new(FtpConnectionManagerAction.Create, null),
            "edit" when result.SelectedItem is not null => new(FtpConnectionManagerAction.Edit, result.SelectedItem),
            "delete" when result.SelectedItem is not null => new(FtpConnectionManagerAction.Delete, result.SelectedItem),
            _ => null,
        };

    private static string FormatConnection(FtpConnectionInfo connection)
    {
        string marker = connection.ShowInDriveSelection ? "*" : " ";
        return $"{marker} {SecurityLabel(connection.SecurityMode)} {connection.DisplayName}  {connection.Username}@{connection.Host}:{connection.Port}";
    }

    private static string SecurityLabel(FtpConnectionSecurityMode mode) =>
        mode switch
        {
            FtpConnectionSecurityMode.PlainFtp => "FTP plain",
            FtpConnectionSecurityMode.ExplicitFtps => "FTPS explicit",
            FtpConnectionSecurityMode.ImplicitFtps => "FTPS implicit",
            FtpConnectionSecurityMode.Auto => "FTP/FTPS auto",
            _ => mode.ToString(),
        };
}
