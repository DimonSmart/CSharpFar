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
    private readonly ModalDialogHost _modalDialogs;

    public FtpConnectionManagerDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    [Obsolete("Use the ModalDialogHost constructor.")]
    public FtpConnectionManagerDialog(ScreenRenderer screen) : this(ModalDialogHost.For(screen)) { }

    public FtpConnectionManagerResult? Show(IReadOnlyList<FtpConnectionInfo> connections)
    {
        var dialog = new ListWithButtonsDialog<FtpConnectionInfo>(
            connections,
            FormatConnection,
            CreateButtons(connections.Count > 0),
            "FTP/FTPS connections")
        {
            DialogWidth = 76,
            MinDialogWidth = 44,
            MaxVisibleRows = 12,
            EmptyText = "No saved FTP/FTPS connections.",
            DefaultListActionId = "connect",
            CancelActionId = "cancel",
            DeleteActionId = "delete",
        };

        var result = dialog.Show(_modalDialogs);
        return result is null ? null : ToManagerResult(result);
    }

    private static IReadOnlyList<DialogButton> CreateButtons(bool hasConnections) =>
        hasConnections
            ? [
                new DialogButton("connect", "Connect", 'O', IsDefault: true),
                new DialogButton("create", "New", 'N'),
                new DialogButton("edit", "Edit", 'E'),
                new DialogButton("delete", "Delete", 'D'),
                new DialogButton("cancel", "Cancel", 'C'),
            ]
            : [
                new DialogButton("create", "New", 'N', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ];

    private static FtpConnectionManagerResult? ToManagerResult(ListWithButtonsDialogResult<FtpConnectionInfo> result) =>
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
