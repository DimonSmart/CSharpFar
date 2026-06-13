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
    private readonly ScreenRenderer _screen;

    public SftpConnectionManagerDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _ = palette;
    }

    public SftpConnectionManagerResult? Show(IReadOnlyList<SftpConnectionInfo> connections)
    {
        var dialog = new ListWithButtonsDialog<SftpConnectionInfo>(
            connections,
            FormatConnection,
            CreateButtons(connections.Count > 0),
            "SFTP connections")
        {
            DialogWidth = 68,
            MinDialogWidth = 40,
            MaxVisibleRows = 12,
            EmptyText = "No saved SFTP connections.",
            DefaultListActionId = "connect",
            CancelActionId = "cancel",
            DeleteActionId = "delete",
        };

        var result = dialog.Show(_screen);
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

    private static SftpConnectionManagerResult? ToManagerResult(ListWithButtonsDialogResult<SftpConnectionInfo> result) =>
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
