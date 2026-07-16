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

internal readonly record struct SftpConnectionFormInputResult(
    FormInputResult FormResult,
    bool EndpointChanged,
    bool SaveOptionsChanged);

internal sealed class SftpConnectionDialog
{
    private const int DialogWidth = 74;
    private const int DialogHeight = 18;
    private const int FieldWidth = 42;

    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogHost _modalDialogs;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SftpConnectionDialog(ModalDialogHost modalDialogs) => _modalDialogs = modalDialogs;

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
            new DialogButton("cancel", "Cancel", 'C'),
        ],
        FarDialogStyles.Fill,
        FarDialogStyles.FocusedInput) { Id = "actions" };
        var form = new ScrollableFormDialog();

        void PrepareRows() => form.SetRows(
            BuildRows(
                request.AllowTemporaryConnection,
                connectionName, host, port, userName, password, remoteRoot,
                histories, connectionNameState, hostState, portState, userNameState, passwordState, remoteRootState,
                saveConnectionRow, savePasswordRow, showInDriveRow, trustHostKeyRow, hostKeyFingerprint),
            [actions]);

        return _modalDialogs.RunInteractive<ScrollableFormFrame, SftpConnectionFormInputResult, SftpConnectionDialogResult?>(
            (context, focusScope) => Draw(context, focusScope, form, connection is null ? "SFTP connection" : "Edit SFTP connection", error),
            form.BuildInteractionFrame,
            (input, frame, route) =>
            {
                string previousHost = host.Text;
                string previousPort = port.Text;
                bool previousSaveConnection = saveConnectionRow.Value;
                bool previousSavePassword = savePasswordRow.Value;
                FormRouteResult result = form.RouteInput(input, frame, route);
                return (new SftpConnectionFormInputResult(
                    result.FormResult,
                    !string.Equals(previousHost, host.Text, StringComparison.Ordinal) || !string.Equals(previousPort, port.Text, StringComparison.Ordinal),
                    previousSaveConnection != saveConnectionRow.Value || previousSavePassword != savePasswordRow.Value), result.UiResult);
            },
            (routed, result) =>
            {
                FormInputResult formResult = result.FormResult;
                if (formResult.IsHandled)
                    error = null;
                if (result.EndpointChanged)
                {
                    hostKeyFingerprint = null;
                    trustHostKeyRow.Value = false;
                }
                if (result.SaveOptionsChanged)
                {
                    if (savePasswordRow.Value)
                        saveConnectionRow.Value = true;
                    if (!saveConnectionRow.Value)
                        savePasswordRow.Value = false;
                }

                if (formResult.Kind == FormInputResultKind.Cancel || routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Escape })
                    return ModalDialogLoopResult<SftpConnectionDialogResult?>.Complete(null);

                if (formResult.Kind == FormInputResultKind.Submit ||
                    routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
                    FormDialogInput.ShouldImplicitlySubmit(routed, formResult, form))
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
                        form.TryFocus("trust-host-key");
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

    private ScrollableFormFrame Draw(UiRenderContext context, UiFocusScope focusScope, ScrollableFormDialog form, string title, string? error)
    {
        ScrollableFormFrame? frame = null;
        _modalRenderer.Render(context.Screen, OuterBounds(context.Size), title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect bounds = layout.FrameBounds;
            int contentX = bounds.X + 2;
            int contentWidth = Math.Max(1, bounds.Width - 4);
            int buttonY = bounds.Bottom - 2;
            int errorY = buttonY - 1;
            frame = form.Render(new FormRenderContext(
                context,
                new Rect(contentX, bounds.Y + 1, contentWidth, Math.Max(1, errorY - bounds.Y - 1)),
                FarDialogStyles.Border,
                new Rect(contentX, buttonY, contentWidth, 1)), focusScope);
            context.Screen.Write(contentX, errorY, Fit(error ?? string.Empty, contentWidth).PadRight(contentWidth), FarDialogStyles.Error);
        });
        return frame ?? throw new InvalidOperationException("SFTP connection dialog did not render a form frame.");
    }

    private static Rect OuterBounds(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(42, size.Width - 2));
        int height = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        return new Rect(Math.Max(0, (size.Width - width) / 2), Math.Max(0, (size.Height - height) / 2), width, height);
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

    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "~";

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
