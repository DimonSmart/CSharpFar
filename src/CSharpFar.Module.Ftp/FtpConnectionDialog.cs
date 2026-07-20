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

internal enum FtpSaveOptionChange { None, SaveConnection, SavePassword }

internal readonly record struct FtpSecurityModeChange(
    FtpConnectionSecurityMode Previous,
    FtpConnectionSecurityMode Current);

internal readonly record struct FtpConnectionFormInputResult(
    FormInputResult FormResult,
    bool EndpointChanged,
    FtpSaveOptionChange SaveOptionChange,
    FtpSecurityModeChange? SecurityModeChange);

internal sealed class FtpConnectionDialog
{
    private const int DialogWidth = 80;
    private const int DialogHeight = 22;
    private const int FieldWidth = 44;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FtpConnectionDialog(ModalDialogHost modalDialogs) => _modalDialogs = modalDialogs;

    public FtpConnectionDialogResult? Show(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        var connection = request.Connection;
        var connectionName = TextBuffer(connection?.DisplayName ?? string.Empty);
        var host = TextBuffer(connection?.Host ?? string.Empty);
        var port = TextBuffer((connection?.Port ?? 21).ToString());
        var username = TextBuffer(connection?.Username ?? string.Empty);
        var password = TextBuffer(request.SavedPassword ?? string.Empty);
        var remoteRoot = TextBuffer(connection?.RemoteRootPath ?? "/");
        var activePorts = TextBuffer(FormatActivePortRange(connection) ?? string.Empty);
        var histories = new TextFieldHistories();
        var nameState = new TextInputRowState();
        var hostState = new TextInputRowState();
        var portState = new TextInputRowState();
        var usernameState = new TextInputRowState();
        var passwordState = new TextInputRowState();
        var rootState = new TextInputRowState();
        var activePortsState = new TextInputRowState();

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
        var activePortsRow = new LabeledTextInputRow("Active ports:", activePorts, histories.ActivePorts, activePortsState, inputWidth: FieldWidth) { Id = "active-ports", SubmitOnEnter = true };
        string? fingerprint = connection?.ExpectedTlsCertificateFingerprint;
        string? error = null;

        dataTls.Value = security.Value != FtpConnectionSecurityMode.PlainFtp && (connection?.UseDataConnectionTls ?? true);
        trust.Value = !string.IsNullOrWhiteSpace(fingerprint);
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var actions = new ButtonRow(
        [
            new DialogButton("submit", submitLabel, request.AllowTemporaryConnection ? 'O' : 'S', IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ], FarDialogStyles.Fill, FarDialogStyles.FocusedInput)
        { Id = "actions" };
        var form = new ScrollableFormDialog();

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
                connectionName, host, port, username, password, remoteRoot, activePorts,
                histories, nameState, hostState, portState, usernameState, passwordState, rootState, activePortsState,
                saveConnection, savePassword, showInDrive, security, dataMode, dataTls, activePortsRow, trust), [actions]);
        }

        PrepareRows();
        return _modalDialogs.RunInteractive<ScrollableFormFrame, FtpConnectionFormInputResult, FtpConnectionDialogResult?>(
            (context, focusScope) => Draw(context, focusScope, form, connection is null ? "FTP/FTPS connection" : "Edit FTP/FTPS connection", error),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                string oldHost = host.Text, oldPort = port.Text;
                bool oldSaveConnection = saveConnection.Value, oldSavePassword = savePassword.Value;
                var oldSecurity = security.Value;
                FormRouteResult routed = form.RouteInput(input, frame, route);
                var result = new FtpConnectionFormInputResult(
                    routed.FormResult,
                    !string.Equals(oldHost, host.Text, StringComparison.Ordinal) || !string.Equals(oldPort, port.Text, StringComparison.Ordinal),
                    (oldSaveConnection != saveConnection.Value, oldSavePassword != savePassword.Value) switch
                    {
                        (false, false) => FtpSaveOptionChange.None,
                        (true, false) => FtpSaveOptionChange.SaveConnection,
                        (false, true) => FtpSaveOptionChange.SavePassword,
                        _ => throw new InvalidOperationException("One routed input event cannot change both FTP save options."),
                    },
                    oldSecurity == security.Value ? null : new FtpSecurityModeChange(oldSecurity, security.Value));
                return (result, routed.UiResult);
            },
            (routed, result) =>
            {
                if (result.FormResult.IsHandled) error = null;
                if (result.EndpointChanged)
                {
                    fingerprint = null;
                    trust.Value = false;
                }
                if (result.SecurityModeChange is { } securityChange)
                {
                    if (port.Text == DefaultPort(securityChange.Previous).ToString())
                        port.SetText(DefaultPort(securityChange.Current).ToString());
                    if (securityChange.Current == FtpConnectionSecurityMode.PlainFtp)
                    {
                        dataTls.Value = false;
                        fingerprint = null;
                        trust.Value = false;
                    }
                    else if (securityChange.Previous == FtpConnectionSecurityMode.PlainFtp)
                        dataTls.Value = true;
                    else { fingerprint = null; trust.Value = false; }
                }
                if (result.SaveOptionChange == FtpSaveOptionChange.SavePassword && savePassword.Value)
                    saveConnection.Value = true;
                else if (result.SaveOptionChange == FtpSaveOptionChange.SaveConnection && !saveConnection.Value)
                    savePassword.Value = false;
                SyncEnabledRows();

                if (result.FormResult.Kind == FormInputResultKind.Cancel)
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);

                bool submit = result.FormResult.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, result.FormResult, form);
                if (!submit)
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;

                if (!TryParseActivePortRange(activePorts.Text.Trim(), dataMode.Value, out int? from, out int? to, out error))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
                var candidate = BuildResult(request, connectionName.Text.Trim(), host.Text.Trim(), port.Text.Trim(), username.Text.Trim(), password.Text,
                    remoteRoot.Text.Trim(), saveConnection.Value, savePassword.Value, showInDrive.Value, security.Value, dataMode.Value,
                    dataTls.Value, from, to, trust.Value ? fingerprint : null);
                if (candidate is null)
                {
                    error = "Host, user name, password, remote root, and valid port are required.";
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
                }
                var validation = validate(candidate);
                if (validation.IsAccepted)
                {
                    histories.Add(connectionName, host, port, username, remoteRoot, activePorts);
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
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }, prepareRender: PrepareRows);
    }

    private ScrollableFormFrame Draw(UiRenderContext context, UiFocusScope focusScope, ScrollableFormDialog form, string title, string? error)
    {
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(context.Screen, OuterBounds(context.Size), title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            int buttonY = layout.FrameBounds.Bottom - 2;
            int errorY = buttonY - 1;
            int contentX = layout.FrameBounds.X + 2;
            int contentWidth = Math.Max(1, layout.FrameBounds.Width - 4);
            frame = form.Render(new FormRenderContext(context, new Rect(contentX, layout.FrameBounds.Y + 1, contentWidth, Math.Max(1, errorY - layout.FrameBounds.Y - 1)), FarDialogStyles.Border, new Rect(contentX, buttonY, contentWidth, 1)), focusScope);
            context.Screen.Write(contentX, errorY, Fit(error ?? string.Empty, contentWidth), FarDialogStyles.Error);
        });
        return frame ?? throw new InvalidOperationException("FTP connection dialog did not render a form frame.");
    }

    private static IReadOnlyList<IFormRow> BuildRows(bool allowTemporary, FtpConnectionSecurityMode securityMode, FtpDataConnectionMode dataMode, string? fingerprint,
        CommandLineState name, CommandLineState host, CommandLineState port, CommandLineState username, CommandLineState password, CommandLineState root, CommandLineState active,
        TextFieldHistories histories, TextInputRowState nameState, TextInputRowState hostState, TextInputRowState portState, TextInputRowState usernameState, TextInputRowState passwordState, TextInputRowState rootState, TextInputRowState activeState,
        CheckBoxRow saveConnection, CheckBoxRow savePassword, CheckBoxRow showInDrive, CompactChoiceFormRow<FtpConnectionSecurityMode> security, CompactChoiceFormRow<FtpDataConnectionMode> dataChoice, CheckBoxRow dataTls, LabeledTextInputRow activeRow, CheckBoxRow trust)
    {
        var rows = new List<IFormRow>
        {
            new LabeledTextInputRow("Connection name:", name, histories.ConnectionName, nameState, inputWidth: FieldWidth) { Id = "connection-name", SubmitOnEnter = true },
            new LabeledTextInputRow("Host:", host, histories.Host, hostState, inputWidth: FieldWidth) { Id = "host", SubmitOnEnter = true },
            new LabeledTextInputRow("Port:", port, histories.Port, portState, inputWidth: FieldWidth) { Id = "port", SubmitOnEnter = true },
            new LabeledTextInputRow("User name:", username, histories.UserName, usernameState, inputWidth: FieldWidth) { Id = "username", SubmitOnEnter = true },
            new LabeledTextInputRow("Password:", password, state: passwordState, inputWidth: FieldWidth, maskInput: true) { Id = "password", SubmitOnEnter = true },
            new LabeledTextInputRow("Remote root:", root, histories.RemoteRoot, rootState, inputWidth: FieldWidth) { Id = "remote-root", SubmitOnEnter = true },
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

    private static CommandLineState TextBuffer(string value) { var result = new CommandLineState(); result.SetText(value); return result; }
    private static string Fit(string text, int width) => width <= 0 ? string.Empty : text.Length <= width ? text.PadRight(width) : text[..width];
    private static Rect OuterBounds(ConsoleSize size) { int width = Math.Min(DialogWidth, Math.Max(48, size.Width - 2)); int height = Math.Min(DialogHeight, Math.Max(8, size.Height - 2)); return new Rect(Math.Max(0, (size.Width - width) / 2), Math.Max(0, (size.Height - height) / 2), width, height); }
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

    private sealed class TextFieldHistories
    {
        public SingleLineTextHistoryState ConnectionName { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.ConnectionName");
        public SingleLineTextHistoryState Host { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.Host");
        public SingleLineTextHistoryState Port { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.Port");
        public SingleLineTextHistoryState UserName { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.UserName");
        public SingleLineTextHistoryState RemoteRoot { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.RemoteRoot");
        public SingleLineTextHistoryState ActivePorts { get; } = HistoryRegistry.GetOrCreate("FtpConnectionDialog.ActivePorts");
        public void Add(params CommandLineState[] fields) { foreach (var field in fields) _ = field; ConnectionName.Add(fields[0].Text.Trim()); Host.Add(fields[1].Text.Trim()); Port.Add(fields[2].Text.Trim()); UserName.Add(fields[3].Text.Trim()); RemoteRoot.Add(fields[4].Text.Trim()); ActivePorts.Add(fields[5].Text.Trim()); }
    }
}
