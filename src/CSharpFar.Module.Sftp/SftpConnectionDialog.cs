using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Module.Sftp;

internal sealed record SftpConnectionDialogRequest(
    SftpConnectionInfo? Connection,
    string? SavedPassword,
    bool SaveConnectionByDefault,
    bool AllowTemporaryConnection);

internal sealed record SftpConnectionDialogResult(
    SftpConnectionInfo Connection,
    string Password,
    bool SaveConnection,
    bool SavePassword,
    string? PreviousCredentialId);

internal sealed record SftpConnectionDialogValidationResult(
    bool IsAccepted,
    string? ErrorMessage,
    string? HostKeyFingerprint)
{
    public static SftpConnectionDialogValidationResult Accepted() => new(true, null, null);
    public static SftpConnectionDialogValidationResult Error(string message) => new(false, message, null);
    public static SftpConnectionDialogValidationResult RequireHostKeyTrust(string fingerprint) =>
        new(false, "Review the host key fingerprint and check Trust host key.", fingerprint);
}

internal sealed class SftpConnectionDialog
{
    private const int DialogWidth = 74;
    private const int DialogHeight = 18;
    private const int FieldWidth = 42;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalFormHost _formDialogs;

    public SftpConnectionDialog(ModalDialogHost modalDialogs) => _formDialogs = new ModalFormHost(modalDialogs);

    public SftpConnectionDialogResult? Show(
        SftpConnectionDialogRequest request,
        Func<SftpConnectionDialogResult, SftpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        return Run(request, validate);
    }

    private SftpConnectionDialogResult? Run(
        SftpConnectionDialogRequest request,
        Func<SftpConnectionDialogResult, SftpConnectionDialogValidationResult> validate)
    {
        SftpConnectionInfo? connection = request.Connection;
        var connectionName = TextBuffer(connection?.DisplayName ?? string.Empty);
        var host = TextBuffer(connection?.Host ?? string.Empty);
        var port = TextBuffer((connection?.Port ?? 22).ToString());
        var userName = TextBuffer(connection?.Username ?? string.Empty);
        var password = TextBuffer(request.SavedPassword ?? string.Empty);
        var remoteRoot = TextBuffer(connection?.RemoteRootPath ?? "/");
        var histories = new TextFieldHistories();
        var connectionNameState = new TextInputRowState();
        var hostState = new TextInputRowState();
        var portState = new TextInputRowState();
        var userNameState = new TextInputRowState();
        var passwordState = new TextInputRowState();
        var remoteRootState = new TextInputRowState();

        var saveConnectionRow = new CheckBoxRow(new CheckBoxLine("Save connection")) { Id = "save-connection" };
        var savePasswordRow = new CheckBoxRow(new CheckBoxLine("Save password")) { Id = "save-password" };
        var showInDriveRow = new CheckBoxRow(new CheckBoxLine("Show in drive menu")) { Id = "show-in-drive" };
        var trustHostKeyRow = new CheckBoxRow(new CheckBoxLine("Trust host key")) { Id = "trust-host-key" };
        saveConnectionRow.Value = request.SaveConnectionByDefault;
        savePasswordRow.Value = connection?.CredentialId is not null && request.SavedPassword is not null;
        showInDriveRow.Value = connection?.ShowInDriveSelection ?? true;

        string? hostKeyFingerprint = connection?.ExpectedHostKeyFingerprint;
        trustHostKeyRow.Value = !string.IsNullOrWhiteSpace(hostKeyFingerprint);
        string? error = null;
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var actions = new ButtonRow(
        [
            new DialogButton("submit", submitLabel, submitLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();

        void PrepareRows() => form.SetRows(
            BuildRows(
                request.AllowTemporaryConnection,
                connectionName, host, port, userName, password, remoteRoot,
                histories, connectionNameState, hostState, portState, userNameState, passwordState, remoteRootState,
                saveConnectionRow, savePasswordRow, showInDriveRow, trustHostKeyRow, hostKeyFingerprint),
            [new LabelRow(error ?? string.Empty, FarDialogStyles.Error), actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                connection is null ? "SFTP connection" : "Edit SFTP connection",
                DialogWidth, DialogHeight, MinWidth: 42, MinHeight: 8),
            static layout =>
            {
                Rect content = layout.ContentBounds;
                int contentX = content.X + 1;
                int contentWidth = Math.Max(1, content.Width - 2);
                return new ModalFormLayout(
                    new Rect(contentX, content.Y, contentWidth, Math.Max(1, content.Height - 2)),
                    new Rect(contentX, content.Bottom - 2, contentWidth, 2));
            },
            (routed, result) =>
            {
                if (result.IsHandled)
                    error = null;
                if (result.Kind == FormInputResultKind.ValueChanged &&
                    (routed.Target == form.GetFocusTarget("host") || routed.Target == form.GetFocusTarget("port")))
                {
                    hostKeyFingerprint = null;
                    trustHostKeyRow.Value = false;
                }
                if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("save-password") && savePasswordRow.Value)
                {
                    saveConnectionRow.Value = true;
                }
                else if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("save-connection") && !saveConnectionRow.Value)
                {
                    savePasswordRow.Value = false;
                }

                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<SftpConnectionDialogResult?>.Complete(null);

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    SftpConnectionDialogResult? candidate = BuildResult(
                        request,
                        connectionName.Text.Trim(), host.Text.Trim(), port.Text.Trim(), userName.Text.Trim(), password.Text,
                        remoteRoot.Text.Trim(), saveConnectionRow.Value, savePasswordRow.Value, showInDriveRow.Value,
                        trustHostKeyRow.Value ? hostKeyFingerprint : null);
                    if (candidate is null)
                    {
                        error = "Host, user name, password, and remote root are required.";
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.Continue;
                    }

                    SftpConnectionDialogValidationResult validation = validate(candidate);
                    if (validation.IsAccepted)
                    {
                        histories.Add(connectionName, host, port, userName, remoteRoot);
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.Complete(candidate);
                    }

                    if (validation.HostKeyFingerprint is not null)
                    {
                        hostKeyFingerprint = validation.HostKeyFingerprint;
                        trustHostKeyRow.Value = false;
                        PrepareRows();
                        error = validation.ErrorMessage;
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.ContinueWithFocus(
                            form.GetFocusTarget("trust-host-key"));
                    }

                    error = validation.ErrorMessage;
                }

                return ModalDialogLoopResult<SftpConnectionDialogResult?>.Continue;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildRows(
        bool allowTemporaryConnection,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        TextFieldHistories histories,
        TextInputRowState connectionNameState,
        TextInputRowState hostState,
        TextInputRowState portState,
        TextInputRowState userNameState,
        TextInputRowState passwordState,
        TextInputRowState remoteRootState,
        CheckBoxRow saveConnectionRow,
        CheckBoxRow savePasswordRow,
        CheckBoxRow showInDriveRow,
        CheckBoxRow trustHostKeyRow,
        string? hostKeyFingerprint)
    {
        var rows = new List<IFormRow>
        {
            new LabeledTextInputRow("Connection name:", connectionName, histories.ConnectionName, connectionNameState, inputWidth: FieldWidth) { Id = "connection-name", SubmitOnEnter = true },
            new LabeledTextInputRow("Host:", host, histories.Host, hostState, inputWidth: FieldWidth) { Id = "host", SubmitOnEnter = true },
            new LabeledTextInputRow("Port:", port, histories.Port, portState, inputWidth: FieldWidth) { Id = "port", SubmitOnEnter = true },
            new LabeledTextInputRow("User name:", userName, histories.UserName, userNameState, inputWidth: FieldWidth) { Id = "username", SubmitOnEnter = true },
            new LabeledTextInputRow("Password:", password, state: passwordState, inputWidth: FieldWidth, maskInput: true) { Id = "password", SubmitOnEnter = true },
            new LabeledTextInputRow("Remote root:", remoteRoot, histories.RemoteRoot, remoteRootState, inputWidth: FieldWidth) { Id = "remote-root", SubmitOnEnter = true },
        };
        if (allowTemporaryConnection)
            rows.Add(saveConnectionRow);
        rows.Add(savePasswordRow);
        rows.Add(showInDriveRow);
        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            rows.Add(new LabeledValueRow("Host key:", () => hostKeyFingerprint, 22) { Id = "host-key-fingerprint" });
            rows.Add(trustHostKeyRow);
        }
        return rows;
    }

    private static SftpConnectionDialogResult? BuildResult(
        SftpConnectionDialogRequest request, string connectionName, string host, string portText, string userName, string password,
        string remoteRoot, bool saveConnection, bool savePassword, bool showInDrive, string? hostKeyFingerprint)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(remoteRoot) || !int.TryParse(portText, out int port) || port is <= 0 or > 65535)
            return null;

        if (!request.AllowTemporaryConnection)
            saveConnection = true;
        if (!saveConnection)
            savePassword = false;

        string connectionId = request.Connection?.Id ?? Guid.NewGuid().ToString("N");
        string? credentialId = savePassword ? request.Connection?.CredentialId ?? connectionId : null;
        return new SftpConnectionDialogResult(new SftpConnectionInfo
        {
            Id = connectionId,
            DisplayName = string.IsNullOrWhiteSpace(connectionName) ? host : connectionName,
            Host = host,
            Port = port,
            Username = userName,
            RemoteRootPath = remoteRoot,
            CredentialId = credentialId,
            ExpectedHostKeyFingerprint = hostKeyFingerprint,
            ShowInDriveSelection = showInDrive,
        }, password, saveConnection, savePassword, request.Connection?.CredentialId);
    }

    private static CommandLineState TextBuffer(string value)
    {
        var buffer = new CommandLineState();
        buffer.SetText(value);
        return buffer;
    }

    private sealed class TextFieldHistories
    {
        public SingleLineTextHistoryState ConnectionName { get; } = HistoryRegistry.GetOrCreate("SftpConnectionDialog.ConnectionName");
        public SingleLineTextHistoryState Host { get; } = HistoryRegistry.GetOrCreate("SftpConnectionDialog.Host");
        public SingleLineTextHistoryState Port { get; } = HistoryRegistry.GetOrCreate("SftpConnectionDialog.Port");
        public SingleLineTextHistoryState UserName { get; } = HistoryRegistry.GetOrCreate("SftpConnectionDialog.UserName");
        public SingleLineTextHistoryState RemoteRoot { get; } = HistoryRegistry.GetOrCreate("SftpConnectionDialog.RemoteRoot");

        public void Add(CommandLineState connectionName, CommandLineState host, CommandLineState port, CommandLineState userName, CommandLineState remoteRoot)
        {
            ConnectionName.Add(connectionName.Text.Trim());
            Host.Add(host.Text.Trim());
            Port.Add(port.Text.Trim());
            UserName.Add(userName.Text.Trim());
            RemoteRoot.Add(remoteRoot.Text.Trim());
        }
    }
}
