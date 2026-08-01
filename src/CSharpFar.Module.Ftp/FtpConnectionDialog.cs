using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Module.Ftp;

internal sealed record FtpConnectionDialogRequest(
    FtpConnectionInfo? Connection,
    string? SavedPassword,
    bool SaveConnectionByDefault,
    bool AllowTemporaryConnection);

internal sealed record FtpConnectionDialogResult(
    FtpConnectionInfo Connection,
    string Password,
    bool SaveConnection,
    bool SavePassword,
    string? PreviousCredentialId);

internal sealed record FtpConnectionDialogValidationResult(
    bool IsAccepted,
    string? ErrorMessage,
    string? CertificateFingerprint)
{
    public static FtpConnectionDialogValidationResult Accepted() => new(true, null, null);
    public static FtpConnectionDialogValidationResult Error(string message) => new(false, message, null);
    public static FtpConnectionDialogValidationResult RequireCertificateTrust(string fingerprint) =>
        new(false, "Review the TLS certificate fingerprint and check Trust certificate.", fingerprint);
}

internal sealed class FtpConnectionDialog
{
    private const int DialogWidth = 80;
    private const int DialogHeight = 22;
    private const int FieldWidth = 44;
    private readonly FormFieldFactory _fields;
    private readonly ModalFormHost _formDialogs;

    public FtpConnectionDialog(ModalDialogHost modalDialogs, FormFieldFactory fields)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    public FtpConnectionDialogResult? Show(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        var connection = request.Connection;
        var state = new FtpFormState(
            _fields.Text("connection-name", connection?.DisplayName ?? string.Empty, FtpTextHistoryIds.ConnectionName, width: FieldWidth, submitOnEnter: true),
            _fields.Text("host", connection?.Host ?? string.Empty, FtpTextHistoryIds.Host, width: FieldWidth, submitOnEnter: true),
            _fields.Text("port", (connection?.Port ?? 21).ToString(), FtpTextHistoryIds.Port, width: FieldWidth, submitOnEnter: true),
            _fields.Text("username", connection?.Username ?? string.Empty, FtpTextHistoryIds.UserName, width: FieldWidth, submitOnEnter: true),
            _fields.Text("password", request.SavedPassword ?? string.Empty, maskInput: true, width: FieldWidth, submitOnEnter: true),
            _fields.Text("remote-root", connection?.RemoteRootPath ?? "/", FtpTextHistoryIds.RemoteRoot, width: FieldWidth, submitOnEnter: true),
            _fields.Text("active-ports", FormatActivePortRange(connection) ?? string.Empty, FtpTextHistoryIds.ActivePorts, width: FieldWidth, submitOnEnter: true),
            new CheckBoxRow("Save connection", request.SaveConnectionByDefault) { Id = "save-connection" },
            new CheckBoxRow("Save password", connection?.CredentialId is not null && request.SavedPassword is not null) { Id = "save-password" },
            new CheckBoxRow("Show in drive menu", connection?.ShowInDriveSelection ?? true) { Id = "show-in-drive" },
            new CheckBoxRow("Use TLS for data connection") { Id = "data-tls" },
            new CheckBoxRow("Trust certificate") { Id = "trust-certificate" },
            new CompactChoiceFormRow<FtpConnectionSecurityMode>("Security", Enum.GetValues<FtpConnectionSecurityMode>(), SecurityLabel, connection?.SecurityMode ?? FtpConnectionSecurityMode.ExplicitFtps) { Id = "security" },
            new CompactChoiceFormRow<FtpDataConnectionMode>("Data mode", Enum.GetValues<FtpDataConnectionMode>(), DataModeLabel, connection?.DataConnectionMode ?? FtpDataConnectionMode.AutoPassive) { Id = "data-mode" },
            request.AllowTemporaryConnection);
        string? fingerprint = connection?.ExpectedTlsCertificateFingerprint;
        string? error = null;

        state.DataTls.Value = state.Security.Value != FtpConnectionSecurityMode.PlainFtp && (connection?.UseDataConnectionTls ?? true);
        state.TrustCertificate.Value = !string.IsNullOrWhiteSpace(fingerprint);
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var actions = new ButtonRow(
        [
            DialogButton.Default("submit", submitLabel, request.AllowTemporaryConnection ? 'O' : 'S'),
            DialogButton.Cancel(),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        FtpConnectionSecurityMode previousSecurity = state.Security.Value;

        void SyncEnabledRows()
        {
            state.DataTls.Enabled = state.Security.Value != FtpConnectionSecurityMode.PlainFtp;
            if (!state.DataTls.Enabled) state.DataTls.Value = false;
            state.TrustCertificate.Enabled = state.Security.Value != FtpConnectionSecurityMode.PlainFtp && !string.IsNullOrWhiteSpace(fingerprint);
            if (!state.TrustCertificate.Enabled) state.TrustCertificate.Value = false;
        }

        void PrepareRows()
        {
            SyncEnabledRows();
            form.SetRows(BuildRows(state, fingerprint),
                FormFooter.ErrorAndButtons(() => error, actions));
        }

        PrepareRows();
        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                connection is null ? "FTP/FTPS connection" : "Edit FTP/FTPS connection",
                DialogWidth, DialogHeight, MinWidth: 48, MinHeight: 8),
            static layout => ModalFormLayout.WithFooter(layout.ContentBounds, footerHeight: 2),
            (routed, result) =>
            {
                if (result.IsHandled) error = null;
                if (result is { Kind: FormInputResultKind.ValueChanged, SourceRowId: "host" or "port" })
                {
                    fingerprint = null;
                    state.TrustCertificate.Value = false;
                }
                if (result is { Kind: FormInputResultKind.ValueChanged, SourceRowId: "security" })
                {
                    if (state.Port.Text == DefaultPort(previousSecurity).ToString())
                        state.Port.Text = DefaultPort(state.Security.Value).ToString();
                    if (state.Security.Value == FtpConnectionSecurityMode.PlainFtp)
                    {
                        state.DataTls.Value = false;
                        fingerprint = null;
                        state.TrustCertificate.Value = false;
                    }
                    else if (previousSecurity == FtpConnectionSecurityMode.PlainFtp)
                        state.DataTls.Value = true;
                    else { fingerprint = null; state.TrustCertificate.Value = false; }
                    previousSecurity = state.Security.Value;
                }
                if (result is { Kind: FormInputResultKind.ValueChanged, SourceRowId: "save-password" } && state.SavePassword.Value)
                    state.SaveConnection.Value = true;
                else if (state.AllowTemporaryConnection &&
                    result is { Kind: FormInputResultKind.ValueChanged, SourceRowId: "save-connection" } &&
                    !state.SaveConnection.Value)
                    state.SavePassword.Value = false;
                SyncEnabledRows();

                if (FormDialogInput.ShouldCancel(result))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);

                if (!FormDialogInput.ShouldSubmit(routed, result, form))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueNoChange;

                if (!TryParseActivePortRange(state.ActivePorts.Text.Trim(), state.DataMode.Value, out int? from, out int? to, out error))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueChanged;
                var candidate = BuildResult(request, state.ConnectionName.Text.Trim(), state.Host.Text.Trim(), state.Port.Text.Trim(), state.UserName.Text.Trim(), state.Password.Text,
                    state.RemoteRoot.Text.Trim(), state.SaveConnection.Value, state.SavePassword.Value, state.ShowInDrive.Value, state.Security.Value, state.DataMode.Value,
                    state.DataTls.Value, from, to, state.TrustCertificate.Value ? fingerprint : null);
                if (candidate is null)
                {
                    error = "Host, user name, password, remote root, and valid port are required.";
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueChanged;
                }
                var validation = validate(candidate);
                if (validation.IsAccepted)
                {
                    state.AcceptHistory();
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(candidate);
                }
                if (validation.CertificateFingerprint is not null)
                {
                    fingerprint = validation.CertificateFingerprint;
                    state.TrustCertificate.Value = false;
                    SyncEnabledRows();
                    PrepareRows();
                    error = validation.ErrorMessage;
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueWithFocus(
                        form.GetFocusTarget("trust-certificate"));
                }
                error = validation.ErrorMessage;
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueChanged;
            }, prepareRender: PrepareRows);
    }

    private static IReadOnlyList<IFormRow> BuildRows(FtpFormState state, string? fingerprint)
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
        if (state.AllowTemporaryConnection) rows.Add(state.SaveConnection);
        rows.Add(state.SavePassword); rows.Add(state.ShowInDrive); rows.Add(state.Security); rows.Add(state.DataMode); rows.Add(state.DataTls);
        if (state.DataMode.Value == FtpDataConnectionMode.Active) rows.Add(state.ActivePorts.AsLabeledRow("Active ports:"));
        rows.Add(new LabeledValueRow("TLS cert:", () => state.Security.Value == FtpConnectionSecurityMode.PlainFtp ? "(plain FTP has no TLS certificate)" : string.IsNullOrWhiteSpace(fingerprint) ? "(press F10 to read certificate)" : fingerprint, 22) { Id = "certificate-fingerprint" });
        rows.Add(state.TrustCertificate);
        return rows;
    }

    private static FtpConnectionDialogResult? BuildResult(FtpConnectionDialogRequest request, string name, string host, string portText, string username, string password, string root,
        bool saveConnection, bool savePassword, bool showInDrive, FtpConnectionSecurityMode security, FtpDataConnectionMode dataMode, bool dataTls, int? from, int? to, string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(root) || !int.TryParse(portText, out int port) || port is <= 0 or > 65535) return null;
        if (!request.AllowTemporaryConnection) saveConnection = true;
        if (!saveConnection) savePassword = false;
        string id = request.Connection?.Id ?? Guid.NewGuid().ToString("N");
        var connection = new FtpConnectionInfo
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(name) ? host : name,
            Host = host,
            Port = port,
            Username = username,
            RemoteRootPath = root,
            CredentialId = savePassword ? request.Connection?.CredentialId ?? $"ftp-{id}" : null,
            SecurityMode = security,
            DataConnectionMode = dataMode,
            UseDataConnectionTls = security != FtpConnectionSecurityMode.PlainFtp && dataTls,
            ExpectedTlsCertificateFingerprint = security == FtpConnectionSecurityMode.PlainFtp ? null : fingerprint,
            ActiveModeLocalPortFrom = from,
            ActiveModeLocalPortTo = to,
            ShowInDriveSelection = showInDrive
        };
        return new FtpConnectionDialogResult(connection, password, saveConnection, savePassword, request.Connection?.CredentialId);
    }

    private static int SecurityIndex(FtpConnectionSecurityMode mode) => Array.IndexOf(Enum.GetValues<FtpConnectionSecurityMode>(), mode);
    private static int DataModeIndex(FtpDataConnectionMode mode) => Array.IndexOf(Enum.GetValues<FtpDataConnectionMode>(), mode);
    private static int DefaultPort(FtpConnectionSecurityMode mode) => mode == FtpConnectionSecurityMode.ImplicitFtps ? 990 : 21;
    private static string SecurityLabel(FtpConnectionSecurityMode mode) => mode switch { FtpConnectionSecurityMode.PlainFtp => "Plain FTP - no TLS", FtpConnectionSecurityMode.ExplicitFtps => "Explicit FTPS", FtpConnectionSecurityMode.ImplicitFtps => "Implicit FTPS", FtpConnectionSecurityMode.Auto => "Auto FTP/FTPS", _ => mode.ToString() };
    private static string DataModeLabel(FtpDataConnectionMode mode) => mode switch { FtpDataConnectionMode.AutoPassive => "Auto passive", FtpDataConnectionMode.Passive => "Passive", FtpDataConnectionMode.Active => "Active", _ => mode.ToString() };
    private static string? FormatActivePortRange(FtpConnectionInfo? connection) => connection?.ActiveModeLocalPortFrom is not { } from || connection.ActiveModeLocalPortTo is not { } to ? null : from == to ? from.ToString() : $"{from}-{to}";
    private static bool TryParseActivePortRange(string text, FtpDataConnectionMode mode, out int? from, out int? to, out string? error)
    {
        from = to = null; error = null; if (mode != FtpDataConnectionMode.Active || string.IsNullOrWhiteSpace(text)) return true;
        string[] parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out int single)) from = to = single;
        else if (parts.Length == 2 && int.TryParse(parts[0], out int start) && int.TryParse(parts[1], out int end)) (from, to) = (start, end);
        else { error = "Active port range must be empty, a port, or start-end."; return false; }
        if (from is <= 0 or > 65535 || to is <= 0 or > 65535 || from > to) { error = "Active port range must be between 1 and 65535, with start not greater than end."; return false; }
        return true;
    }

    private sealed record FtpFormState(TextField ConnectionName, TextField Host, TextField Port,
        TextField UserName, TextField Password, TextField RemoteRoot, TextField ActivePorts,
        CheckBoxRow SaveConnection, CheckBoxRow SavePassword, CheckBoxRow ShowInDrive,
        CheckBoxRow DataTls, CheckBoxRow TrustCertificate,
        CompactChoiceFormRow<FtpConnectionSecurityMode> Security,
        CompactChoiceFormRow<FtpDataConnectionMode> DataMode,
        bool AllowTemporaryConnection)
    {
        public void AcceptHistory()
        {
            ConnectionName.AcceptHistory(); Host.AcceptHistory(); Port.AcceptHistory();
            UserName.AcceptHistory(); RemoteRoot.AcceptHistory(); ActivePorts.AcceptHistory();
        }
    }

}
