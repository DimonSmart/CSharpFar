using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
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
    private readonly ITextFieldHistoryProvider _historyRegistry;
    private readonly ModalFormHost _formDialogs;

    public FtpConnectionDialog(ModalDialogHost modalDialogs, ITextFieldHistoryProvider historyRegistry)
    {
        _formDialogs = new ModalFormHost(modalDialogs);
        _historyRegistry = historyRegistry ?? throw new ArgumentNullException(nameof(historyRegistry));
    }

    public FtpConnectionDialogResult? Show(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        var connection = request.Connection;
        var fields = new FormFieldFactory(_historyRegistry);
        var state = new FtpFormState(
            fields.Text("connection-name", connection?.DisplayName ?? string.Empty, FtpTextHistoryIds.ConnectionName, width: FieldWidth, submitOnEnter: true),
            fields.Text("host", connection?.Host ?? string.Empty, FtpTextHistoryIds.Host, width: FieldWidth, submitOnEnter: true),
            fields.Text("port", (connection?.Port ?? 21).ToString(), FtpTextHistoryIds.Port, width: FieldWidth, submitOnEnter: true),
            fields.Text("username", connection?.Username ?? string.Empty, FtpTextHistoryIds.UserName, width: FieldWidth, submitOnEnter: true),
            fields.Text("password", request.SavedPassword ?? string.Empty, maskInput: true, width: FieldWidth, submitOnEnter: true),
            fields.Text("remote-root", connection?.RemoteRootPath ?? "/", FtpTextHistoryIds.RemoteRoot, width: FieldWidth, submitOnEnter: true),
            fields.Text("active-ports", FormatActivePortRange(connection) ?? string.Empty, FtpTextHistoryIds.ActivePorts, width: FieldWidth, submitOnEnter: true));
        var connectionName = state.ConnectionName; var host = state.Host; var port = state.Port; var username = state.UserName;
        var password = state.Password; var remoteRoot = state.RemoteRoot; var activePorts = state.ActivePorts;

        var saveConnection = new CheckBoxRow(new CheckBoxLine("Save connection")) { Id = "save-connection", Value = request.SaveConnectionByDefault };
        var savePassword = new CheckBoxRow(new CheckBoxLine("Save password")) { Id = "save-password", Value = connection?.CredentialId is not null && request.SavedPassword is not null };
        var showInDrive = new CheckBoxRow(new CheckBoxLine("Show in drive menu")) { Id = "show-in-drive", Value = connection?.ShowInDriveSelection ?? true };
        var dataTls = new CheckBoxRow(new CheckBoxLine("Use TLS for data connection")) { Id = "data-tls" };
        var trust = new CheckBoxRow(new CheckBoxLine("Trust certificate")) { Id = "trust-certificate" };
        var security = new CompactChoiceFormRow<FtpConnectionSecurityMode>(
            new ChoiceRow<FtpConnectionSecurityMode>(Enum.GetValues<FtpConnectionSecurityMode>(), SecurityLabel, SecurityIndex(connection?.SecurityMode ?? FtpConnectionSecurityMode.ExplicitFtps)), "Security")
        { Id = "security" };
        var dataMode = new CompactChoiceFormRow<FtpDataConnectionMode>(
            new ChoiceRow<FtpDataConnectionMode>(Enum.GetValues<FtpDataConnectionMode>(), DataModeLabel, DataModeIndex(connection?.DataConnectionMode ?? FtpDataConnectionMode.AutoPassive)), "Data mode")
        { Id = "data-mode" };
        var activePortsRow = new LabeledTextInputRow("Active ports:", activePorts, inputWidth: FieldWidth);
        string? fingerprint = connection?.ExpectedTlsCertificateFingerprint;
        string? error = null;

        dataTls.Value = security.Value != FtpConnectionSecurityMode.PlainFtp && (connection?.UseDataConnectionTls ?? true);
        trust.Value = !string.IsNullOrWhiteSpace(fingerprint);
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var actions = new ButtonRow(
        [
            new DialogButton("submit", submitLabel, request.AllowTemporaryConnection ? 'O' : 'S', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C', Role: DialogButtonRole.Cancel),
        ])
        { Id = "actions" };
        var form = new ScrollableFormDialog();
        FtpConnectionSecurityMode previousSecurity = security.Value;

        void SyncEnabledRows()
        {
            dataTls.Enabled = security.Value != FtpConnectionSecurityMode.PlainFtp;
            if (!dataTls.Enabled) dataTls.Value = false;
            trust.Enabled = security.Value != FtpConnectionSecurityMode.PlainFtp && !string.IsNullOrWhiteSpace(fingerprint);
            if (!trust.Enabled) trust.Value = false;
        }

        void PrepareRows()
        {
            SyncEnabledRows();
            form.SetRows(BuildRows(request.AllowTemporaryConnection, security.Value, dataMode.Value, fingerprint,
                connectionName, host, port, username, password, remoteRoot,
                saveConnection, savePassword, showInDrive, security, dataMode, dataTls, activePortsRow, trust),
                [new LabelRow(error ?? string.Empty, FarDialogStyles.Error), actions]);
        }

        PrepareRows();
        return _formDialogs.Run(
            form,
            new ModalFormOptions(
                connection is null ? "FTP/FTPS connection" : "Edit FTP/FTPS connection",
                DialogWidth, DialogHeight, MinWidth: 48, MinHeight: 8),
            static layout =>
            {
                Rect content = layout.ContentBounds;
                return new ModalFormLayout(
                    new Rect(content.X, content.Y, content.Width, Math.Max(1, content.Height - 2)),
                    new Rect(content.X, content.Bottom - 2, content.Width, 2));
            },
            (routed, result) =>
            {
                if (result.IsHandled) error = null;
                if (result.Kind == FormInputResultKind.ValueChanged &&
                    (routed.Target == form.GetFocusTarget("host") || routed.Target == form.GetFocusTarget("port")))
                {
                    fingerprint = null;
                    trust.Value = false;
                }
                if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("security"))
                {
                    if (port.Text == DefaultPort(previousSecurity).ToString())
                        port.Text = DefaultPort(security.Value).ToString();
                    if (security.Value == FtpConnectionSecurityMode.PlainFtp)
                    {
                        dataTls.Value = false;
                        fingerprint = null;
                        trust.Value = false;
                    }
                    else if (previousSecurity == FtpConnectionSecurityMode.PlainFtp)
                        dataTls.Value = true;
                    else { fingerprint = null; trust.Value = false; }
                    previousSecurity = security.Value;
                }
                if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("save-password") && savePassword.Value)
                    saveConnection.Value = true;
                else if (result.Kind == FormInputResultKind.ValueChanged && routed.Target == form.GetFocusTarget("save-connection") && !saveConnection.Value)
                    savePassword.Value = false;
                SyncEnabledRows();

                if (result.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);

                bool submit = result.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                FormDialogInput.ShouldImplicitlySubmit(routed, result, form);
                if (!submit)
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueNoChange;

                if (!TryParseActivePortRange(activePorts.Text.Trim(), dataMode.Value, out int? from, out int? to, out error))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.ContinueChanged;
                var candidate = BuildResult(request, connectionName.Text.Trim(), host.Text.Trim(), port.Text.Trim(), username.Text.Trim(), password.Text,
                    remoteRoot.Text.Trim(), saveConnection.Value, savePassword.Value, showInDrive.Value, security.Value, dataMode.Value,
                    dataTls.Value, from, to, trust.Value ? fingerprint : null);
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
                    trust.Value = false;
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

    private static IReadOnlyList<IFormRow> BuildRows(bool allowTemporary, FtpConnectionSecurityMode securityMode, FtpDataConnectionMode dataMode, string? fingerprint,
        TextField name, TextField host, TextField port, TextField username, TextField password, TextField root,
        CheckBoxRow saveConnection, CheckBoxRow savePassword, CheckBoxRow showInDrive, CompactChoiceFormRow<FtpConnectionSecurityMode> security, CompactChoiceFormRow<FtpDataConnectionMode> dataChoice, CheckBoxRow dataTls, LabeledTextInputRow activeRow, CheckBoxRow trust)
    {
        var rows = new List<IFormRow>
        {
            new LabeledTextInputRow("Connection name:", name, inputWidth: FieldWidth),
            new LabeledTextInputRow("Host:", host, inputWidth: FieldWidth),
            new LabeledTextInputRow("Port:", port, inputWidth: FieldWidth),
            new LabeledTextInputRow("User name:", username, inputWidth: FieldWidth),
            new LabeledTextInputRow("Password:", password, inputWidth: FieldWidth, maskInput: true),
            new LabeledTextInputRow("Remote root:", root, inputWidth: FieldWidth),
        };
        if (allowTemporary) rows.Add(saveConnection);
        rows.Add(savePassword); rows.Add(showInDrive); rows.Add(security); rows.Add(dataChoice); rows.Add(dataTls);
        if (dataMode == FtpDataConnectionMode.Active) rows.Add(activeRow);
        rows.Add(new LabeledValueRow("TLS cert:", () => securityMode == FtpConnectionSecurityMode.PlainFtp ? "(plain FTP has no TLS certificate)" : string.IsNullOrWhiteSpace(fingerprint) ? "(press F10 to read certificate)" : fingerprint, 22) { Id = "certificate-fingerprint" });
        rows.Add(trust);
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
        TextField UserName, TextField Password, TextField RemoteRoot, TextField ActivePorts)
    {
        public void AcceptHistory()
        {
            ConnectionName.AcceptHistory(); Host.AcceptHistory(); Port.AcceptHistory();
            UserName.AcceptHistory(); RemoteRoot.AcceptHistory(); ActivePorts.AcceptHistory();
        }
    }

}
