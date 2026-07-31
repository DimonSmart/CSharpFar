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

    private readonly ITextFieldHistoryProvider _historyRegistry;
    private readonly ModalFormHost _formDialogs;

    public SftpConnectionDialog(ModalDialogHost modalDialogs, ITextFieldHistoryProvider historyRegistry)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _historyRegistry = historyRegistry ?? throw new ArgumentNullException(nameof(historyRegistry));
    }

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
        var fields = new FormFieldFactory(_historyRegistry);
        var state = new SftpFormState(
            fields.Text("connection-name", connection?.DisplayName ?? string.Empty, SftpTextHistoryIds.ConnectionName, width: FieldWidth, submitOnEnter: true),
            fields.Text("host", connection?.Host ?? string.Empty, SftpTextHistoryIds.Host, width: FieldWidth, submitOnEnter: true),
            fields.Text("port", (connection?.Port ?? 22).ToString(), SftpTextHistoryIds.Port, width: FieldWidth, submitOnEnter: true),
            fields.Text("username", connection?.Username ?? string.Empty, SftpTextHistoryIds.UserName, width: FieldWidth, submitOnEnter: true),
            fields.Text("password", request.SavedPassword ?? string.Empty, maskInput: true, width: FieldWidth, submitOnEnter: true),
            fields.Text("remote-root", connection?.RemoteRootPath ?? "/", SftpTextHistoryIds.RemoteRoot, width: FieldWidth, submitOnEnter: true),
            new CheckBoxRow("Save connection", request.SaveConnectionByDefault) { Id = "save-connection" },
            new CheckBoxRow("Save password", connection?.CredentialId is not null && request.SavedPassword is not null) { Id = "save-password" },
            new CheckBoxRow("Show in drive menu", connection?.ShowInDriveSelection ?? true) { Id = "show-in-drive" },
            new CheckBoxRow("Trust host key") { Id = "trust-host-key" },
            request.AllowTemporaryConnection);

        string? hostKeyFingerprint = connection?.ExpectedHostKeyFingerprint;
        state.TrustHostKey.Value = !string.IsNullOrWhiteSpace(hostKeyFingerprint);
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
            BuildRows(state, hostKeyFingerprint),
            [new LabelRow(error ?? string.Empty, FarDialogStyles.Error), actions]);

        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                connection is null ? "SFTP connection" : "Edit SFTP connection",
                DialogWidth, DialogHeight, MinWidth: 42, MinHeight: 8),
            static layout =>
            {
                Rect content = layout.ContentBounds;
                return new ModalFormLayout(
                    new Rect(content.X, content.Y, content.Width, Math.Max(1, content.Height - 2)),
                    new Rect(content.X, content.Bottom - 2, content.Width, 2));
            },
            (routed, result) =>
            {
                if (result.IsHandled)
                    error = null;
                if (result.Kind == FormInputResultKind.ValueChanged &&
                    (routed.Target == form.GetFocusTarget("host") || routed.Target == form.GetFocusTarget("port")))
                {
                    hostKeyFingerprint = null;
                    state.TrustHostKey.Value = false;
                }
                if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("save-password") && state.SavePassword.Value)
                {
                    state.SaveConnection.Value = true;
                }
                else if (state.AllowTemporaryConnection &&
                    result.Kind == FormInputResultKind.ValueChanged &&
                    routed.Target == form.GetFocusTarget("save-connection") &&
                    !state.SaveConnection.Value)
                {
                    state.SavePassword.Value = false;
                }

                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<SftpConnectionDialogResult?>.Complete(null);

                if (result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result, form))
                {
                    SftpConnectionDialogResult? candidate = BuildResult(
                        request,
                        state.ConnectionName.Text.Trim(), state.Host.Text.Trim(), state.Port.Text.Trim(), state.UserName.Text.Trim(), state.Password.Text,
                        state.RemoteRoot.Text.Trim(), state.SaveConnection.Value, state.SavePassword.Value, state.ShowInDrive.Value,
                        state.TrustHostKey.Value ? hostKeyFingerprint : null);
                    if (candidate is null)
                    {
                        error = "Host, user name, password, and remote root are required.";
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.ContinueChanged;
                    }

                    SftpConnectionDialogValidationResult validation = validate(candidate);
                    if (validation.IsAccepted)
                    {
                        state.AcceptHistory();
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.Complete(candidate);
                    }

                    if (validation.HostKeyFingerprint is not null)
                    {
                        hostKeyFingerprint = validation.HostKeyFingerprint;
                        state.TrustHostKey.Value = false;
                        PrepareRows();
                        error = validation.ErrorMessage;
                        return ModalDialogLoopResult<SftpConnectionDialogResult?>.ContinueWithFocus(
                            form.GetFocusTarget("trust-host-key"));
                    }

                    error = validation.ErrorMessage;
                }

                return ModalDialogLoopResult<SftpConnectionDialogResult?>.ContinueChanged;
            },
            prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildRows(SftpFormState state, string? hostKeyFingerprint)
    {
        var rows = new List<IFormRow>
        {
            state.ConnectionName.AsLabeledRow("Connection name:"),
            state.Host.AsLabeledRow("Host:"),
            state.Port.AsLabeledRow("Port:"),
            state.UserName.AsLabeledRow("User name:"),
            state.Password.AsLabeledRow("Password:"),
            state.RemoteRoot.AsLabeledRow("Remote root:"),
        };
        if (state.AllowTemporaryConnection)
            rows.Add(state.SaveConnection);
        rows.Add(state.SavePassword);
        rows.Add(state.ShowInDrive);
        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            rows.Add(new LabeledValueRow("Host key:", () => hostKeyFingerprint, 22) { Id = "host-key-fingerprint" });
            rows.Add(state.TrustHostKey);
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

    private sealed record SftpFormState(TextField ConnectionName, TextField Host, TextField Port,
        TextField UserName, TextField Password, TextField RemoteRoot, CheckBoxRow SaveConnection,
        CheckBoxRow SavePassword, CheckBoxRow ShowInDrive, CheckBoxRow TrustHostKey,
        bool AllowTemporaryConnection)
    {
        public void AcceptHistory()
        {
            ConnectionName.AcceptHistory(); Host.AcceptHistory(); Port.AcceptHistory();
            UserName.AcceptHistory(); RemoteRoot.AcceptHistory();
        }
    }

}
