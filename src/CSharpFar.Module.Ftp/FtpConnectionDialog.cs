using CSharpFar.Ui;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

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
    public static FtpConnectionDialogValidationResult Accepted() =>
        new(true, null, null);

    public static FtpConnectionDialogValidationResult Error(string message) =>
        new(false, message, null);

    public static FtpConnectionDialogValidationResult RequireCertificateTrust(string fingerprint) =>
        new(false, "Review the TLS certificate fingerprint and check Trust certificate.", fingerprint);
}

internal sealed class FtpConnectionDialog
{
    private const int DialogWidth = 80;
    private const int DialogHeight = 22;
    private const int FieldWidth = 44;

    private const int RowConnectionName = 0;
    private const int RowHost = 1;
    private const int RowPort = 2;
    private const int RowUserName = 3;
    private const int RowPassword = 4;
    private const int RowRemoteRoot = 5;
    private const int RowSaveConnection = 6;
    private const int RowSavePassword = 7;
    private const int RowShowInDrive = 8;
    private const int RowSecurity = 9;
    private const int RowDataMode = 10;
    private const int RowDataTls = 11;
    private const int RowActivePorts = 12;
    private const int RowFingerprint = 13;
    private const int RowTrustCertificate = 14;
    private const int RowButtons = 15;
    private const int FocusRowCount = 16;

    private readonly ModalDialogHost _modalDialogs;
    private ScreenRenderer _screen => _modalDialogs.Screen;
    private static readonly SingleLineTextHistoryRegistry HistoryRegistry = new();
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FtpConnectionDialog(ModalDialogHost modalDialogs)
    {
        _modalDialogs = modalDialogs;
    }

    public FtpConnectionDialogResult? Show(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        return RunLoop(request, validate);
    }

    private FtpConnectionDialogResult? RunLoop(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        var connection = request.Connection;
        var connectionName = TextBuffer(connection?.DisplayName ?? string.Empty);
        var host = TextBuffer(connection?.Host ?? string.Empty);
        var port = TextBuffer((connection?.Port ?? 21).ToString());
        var userName = TextBuffer(connection?.Username ?? string.Empty);
        var password = TextBuffer(request.SavedPassword ?? string.Empty);
        var remoteRoot = TextBuffer(connection?.RemoteRootPath ?? "/");
        var activePorts = TextBuffer(FormatActivePortRange(connection) ?? string.Empty);
        var histories = new TextFieldHistories();

        bool saveConnection = request.SaveConnectionByDefault;
        bool savePassword = connection?.CredentialId is not null && request.SavedPassword is not null;
        bool showInDrive = connection?.ShowInDriveSelection ?? true;
        var securityMode = connection?.SecurityMode ?? FtpConnectionSecurityMode.ExplicitFtps;
        var dataMode = connection?.DataConnectionMode ?? FtpDataConnectionMode.AutoPassive;
        bool useDataTls = connection?.UseDataConnectionTls ?? true;
        if (securityMode == FtpConnectionSecurityMode.PlainFtp)
            useDataTls = false;
        string? certificateFingerprint = connection?.ExpectedTlsCertificateFingerprint;
        bool trustCertificate = !string.IsNullOrWhiteSpace(certificateFingerprint);
        string? error = null;
        int focusRow = RowConnectionName;
        int bodyScrollTop = 0;
        int focusedButton = 0;
        bool ensureFocusVisible = true;
        ScrollBarDragState? bodyScrollbarDrag = null;
        ScrollBarDragState? historyScrollbarDrag = null;
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("submit", submitLabel, submitLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);
        return _modalDialogs.Run(
            context =>
            {
                var geometry = GetDialogGeometry(context.Size);
                int effectiveBodyScrollTop = ensureFocusVisible
                    ? NormalizeBodyScroll(geometry, focusRow, bodyScrollTop, request.AllowTemporaryConnection, dataMode)
                    : ClampBodyScroll(geometry, bodyScrollTop, request.AllowTemporaryConnection, dataMode);
                var buttons = Draw(
                    geometry,
                    connection is null ? "FTP/FTPS connection" : "Edit FTP/FTPS connection",
                    focusRow, effectiveBodyScrollTop, buttonBar, focusedButton, connectionName, host, port,
                    userName, password, remoteRoot, activePorts, histories, saveConnection, savePassword,
                    showInDrive, securityMode, dataMode, useDataTls, certificateFingerprint,
                    trustCertificate, request.AllowTemporaryConnection, error);
                return new FtpConnectionFrame(context.Size, geometry, effectiveBodyScrollTop, buttons);
            },
            (input, frame) =>
            {
            if (input is MouseConsoleInputEvent historyMouse &&
                TryHandleHistoryDropdownMouse(
                    historyMouse,
                    frame,
                    connectionName,
                    host,
                    port,
                    userName,
                    remoteRoot,
                    activePorts,
                    histories,
                    request.AllowTemporaryConnection,
                    dataMode,
                    ref historyScrollbarDrag))
            {
                ensureFocusVisible = false;
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }

            if (input is MouseConsoleInputEvent scrollbarMouse &&
                TryHandleBodyScrollbarMouse(
                    scrollbarMouse,
                    frame,
                    request.AllowTemporaryConnection,
                    dataMode,
                    ref bodyScrollTop,
                    ref bodyScrollbarDrag))
            {
                ensureFocusVisible = false;
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }

            if ((focusRow == RowButtons || input is MouseConsoleInputEvent) &&
                buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
            {
                focusRow = RowButtons;
                if (buttonId == "cancel")
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);
                if (buttonId == "submit" && TrySubmit(out var submitResult))
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(submitResult);
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                TryHandleMouse(
                    mouse,
                    frame,
                    request.AllowTemporaryConnection,
                    connectionName,
                    host,
                    port,
                    userName,
                    password,
                    remoteRoot,
                    activePorts,
                    histories,
                    ref focusRow,
                    ref saveConnection,
                    ref savePassword,
                    ref showInDrive,
                    ref securityMode,
                    ref dataMode,
                    ref useDataTls,
                    ref certificateFingerprint,
                    ref trustCertificate,
                    ref error);
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;

            if (histories.ForRow(focusRow)?.IsDropdownOpen == true &&
                key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape)
            {
                ClearCertificateWhenEndpointChanges(
                    focusRow,
                    EditFocusedText(
                        focusRow,
                        key,
                        connectionName,
                        host,
                        port,
                        userName,
                        password,
                        remoteRoot,
                        activePorts,
                        histories,
                        frame,
                        request.AllowTemporaryConnection,
                        dataMode,
                    ref error),
                    ref certificateFingerprint,
                    ref trustCertificate);
                return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            }

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);
                case ConsoleKey.F10:
                    if (TrySubmit(out var f10Result))
                        return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(f10Result);
                    break;
                case ConsoleKey.Enter:
                    if (focusRow == RowButtons)
                    {
                        if (focusedButton == 1)
                            return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);
                        if (TrySubmit(out var enterResult))
                            return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(enterResult);
                    }
                    else if (TryToggle(
                        focusRow,
                        request.AllowTemporaryConnection,
                        ref saveConnection,
                        ref savePassword,
                        ref showInDrive,
                        ref securityMode,
                        ref dataMode,
                        ref useDataTls,
                        ref certificateFingerprint,
                        ref trustCertificate,
                        port))
                    {
                        focusRow = CoerceFocusableRow(focusRow, request.AllowTemporaryConnection, dataMode);
                        error = null;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = PreviousRow(focusRow, request.AllowTemporaryConnection, dataMode);
                    error = null;
                    break;
                case ConsoleKey.DownArrow:
                    focusRow = NextRow(focusRow, request.AllowTemporaryConnection, dataMode);
                    error = null;
                    break;
                case ConsoleKey.Tab:
                    focusRow = IsShiftTab(key)
                        ? PreviousRow(focusRow, request.AllowTemporaryConnection, dataMode)
                        : NextRow(focusRow, request.AllowTemporaryConnection, dataMode);
                    error = null;
                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    if (focusRow == RowButtons)
                        buttonBar.TryHandleKey(key, ref focusedButton, out _);
                    else
                        ClearCertificateWhenEndpointChanges(
                            focusRow,
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, histories, frame, request.AllowTemporaryConnection, dataMode, ref error),
                            ref certificateFingerprint,
                            ref trustCertificate);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == RowButtons)
                    {
                        if (focusedButton == 1)
                            return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(null);
                        if (TrySubmit(out var spaceResult))
                            return ModalDialogLoopResult<FtpConnectionDialogResult?>.Complete(spaceResult);
                    }
                    else if (TryToggle(
                        focusRow,
                        request.AllowTemporaryConnection,
                        ref saveConnection,
                        ref savePassword,
                        ref showInDrive,
                        ref securityMode,
                        ref dataMode,
                        ref useDataTls,
                        ref certificateFingerprint,
                        ref trustCertificate,
                        port))
                    {
                        focusRow = CoerceFocusableRow(focusRow, request.AllowTemporaryConnection, dataMode);
                        error = null;
                    }
                    else
                    {
                        ClearCertificateWhenEndpointChanges(
                            focusRow,
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, histories, frame, request.AllowTemporaryConnection, dataMode, ref error),
                            ref certificateFingerprint,
                            ref trustCertificate);
                    }
                    break;
                default:
                    ClearCertificateWhenEndpointChanges(
                        focusRow,
                        EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, histories, frame, request.AllowTemporaryConnection, dataMode, ref error),
                        ref certificateFingerprint,
                        ref trustCertificate);
                    break;
            }

            return ModalDialogLoopResult<FtpConnectionDialogResult?>.Continue;
            },
            applyCommittedFrame: frame =>
            {
                bodyScrollTop = frame.BodyScrollTop;
                ensureFocusVisible = true;
            });

        bool TrySubmit(out FtpConnectionDialogResult? result)
        {
            if (!TryParseActivePortRange(activePorts.Text.Trim(), dataMode, out int? activePortFrom, out int? activePortTo, out error))
            {
                result = null;
                return false;
            }

            result = BuildResult(
                request,
                connectionName.Text.Trim(),
                host.Text.Trim(),
                port.Text.Trim(),
                userName.Text.Trim(),
                password.Text,
                remoteRoot.Text.Trim(),
                saveConnection,
                savePassword,
                showInDrive,
                securityMode,
                dataMode,
                securityMode != FtpConnectionSecurityMode.PlainFtp && useDataTls,
                activePortFrom,
                activePortTo,
                trustCertificate ? certificateFingerprint : null);
            if (result is null)
            {
                error = "Host, user name, password, remote root, and valid port are required.";
                return false;
            }

            var validation = validate(result);
            if (validation.CertificateFingerprint is not null)
            {
                certificateFingerprint = validation.CertificateFingerprint;
                trustCertificate = false;
                focusRow = RowTrustCertificate;
            }

            if (validation.IsAccepted)
            {
                histories.Add(connectionName, host, port, userName, remoteRoot, activePorts);
                return true;
            }

            error = validation.ErrorMessage;
            result = null;
            return false;
        }
    }

    private static CommandLineState TextBuffer(string value)
    {
        var buffer = new CommandLineState();
        buffer.SetText(value);
        return buffer;
    }

    private static bool IsShiftTab(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Tab &&
        (key.Modifiers & ConsoleModifiers.Shift) != 0;

    private static FtpConnectionDialogResult? BuildResult(
        FtpConnectionDialogRequest request,
        string connectionName,
        string host,
        string portText,
        string userName,
        string password,
        string remoteRoot,
        bool saveConnection,
        bool savePassword,
        bool showInDrive,
        FtpConnectionSecurityMode securityMode,
        FtpDataConnectionMode dataMode,
        bool useDataTls,
        int? activePortFrom,
        int? activePortTo,
        string? certificateFingerprint)
    {
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(userName) ||
            string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(remoteRoot) ||
            !int.TryParse(portText, out int port) ||
            port is <= 0 or > 65535)
        {
            return null;
        }

        if (!request.AllowTemporaryConnection)
            saveConnection = true;
        if (!saveConnection)
            savePassword = false;

        string connectionId = request.Connection?.Id ?? Guid.NewGuid().ToString("N");
        string? credentialId = savePassword
            ? request.Connection?.CredentialId ?? $"ftp-{connectionId}"
            : null;

        var connection = new FtpConnectionInfo
        {
            Id = connectionId,
            DisplayName = string.IsNullOrWhiteSpace(connectionName) ? host : connectionName,
            Host = host,
            Port = port,
            Username = userName,
            RemoteRootPath = remoteRoot,
            CredentialId = credentialId,
            SecurityMode = securityMode,
            DataConnectionMode = dataMode,
            UseDataConnectionTls = useDataTls,
            ExpectedTlsCertificateFingerprint = certificateFingerprint,
            ActiveModeLocalPortFrom = activePortFrom,
            ActiveModeLocalPortTo = activePortTo,
            ShowInDriveSelection = showInDrive,
        };

        return new FtpConnectionDialogResult(
            connection,
            password,
            saveConnection,
            savePassword,
            request.Connection?.CredentialId);
    }

    private static int NextRow(int focusRow, bool allowTemporaryConnection, FtpDataConnectionMode dataMode)
    {
        do
        {
            focusRow = (focusRow + 1) % FocusRowCount;
        } while (!IsFocusableRow(focusRow, allowTemporaryConnection, dataMode));

        return focusRow;
    }

    private static int PreviousRow(int focusRow, bool allowTemporaryConnection, FtpDataConnectionMode dataMode)
    {
        do
        {
            focusRow = focusRow == 0 ? FocusRowCount - 1 : focusRow - 1;
        } while (!IsFocusableRow(focusRow, allowTemporaryConnection, dataMode));

        return focusRow;
    }

    private static int CoerceFocusableRow(int focusRow, bool allowTemporaryConnection, FtpDataConnectionMode dataMode) =>
        IsFocusableRow(focusRow, allowTemporaryConnection, dataMode)
            ? focusRow
            : NextRow(focusRow, allowTemporaryConnection, dataMode);

    private static bool IsFocusableRow(int focusRow, bool allowTemporaryConnection, FtpDataConnectionMode dataMode) =>
        (focusRow != RowSaveConnection || allowTemporaryConnection) &&
        (focusRow != RowActivePorts || dataMode == FtpDataConnectionMode.Active);

    private static bool TryToggle(
        int focusRow,
        bool allowTemporaryConnection,
        ref bool saveConnection,
        ref bool savePassword,
        ref bool showInDrive,
        ref FtpConnectionSecurityMode securityMode,
        ref FtpDataConnectionMode dataMode,
        ref bool useDataTls,
        ref string? certificateFingerprint,
        ref bool trustCertificate,
        CommandLineState port)
    {
        switch (focusRow)
        {
            case RowSaveConnection:
                if (!allowTemporaryConnection)
                    return true;
                saveConnection = !saveConnection;
                if (!saveConnection)
                    savePassword = false;
                return true;
            case RowSavePassword:
                savePassword = !savePassword;
                if (savePassword)
                    saveConnection = true;
                return true;
            case RowShowInDrive:
                showInDrive = !showInDrive;
                return true;
            case RowSecurity:
            {
                var previousMode = securityMode;
                securityMode = NextSecurityMode(securityMode);
                if (port.Text == DefaultPort(previousMode).ToString())
                    port.SetText(DefaultPort(securityMode).ToString());
                if (securityMode == FtpConnectionSecurityMode.PlainFtp)
                {
                    useDataTls = false;
                    certificateFingerprint = null;
                    trustCertificate = false;
                }
                else if (previousMode == FtpConnectionSecurityMode.PlainFtp)
                {
                    useDataTls = true;
                }
                else
                {
                    certificateFingerprint = null;
                    trustCertificate = false;
                }
                return true;
            }
            case RowDataMode:
                dataMode = dataMode switch
                {
                    FtpDataConnectionMode.AutoPassive => FtpDataConnectionMode.Passive,
                    FtpDataConnectionMode.Passive => FtpDataConnectionMode.Active,
                    _ => FtpDataConnectionMode.AutoPassive,
                };
                return true;
            case RowDataTls:
                if (securityMode != FtpConnectionSecurityMode.PlainFtp)
                    useDataTls = !useDataTls;
                return true;
            case RowTrustCertificate:
                if (!string.IsNullOrWhiteSpace(certificateFingerprint) &&
                    securityMode != FtpConnectionSecurityMode.PlainFtp)
                {
                    trustCertificate = !trustCertificate;
                }
                return true;
            default:
                return false;
        }
    }

    private static bool EditFocusedText(
        int focusRow,
        ConsoleKeyInfo key,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        TextFieldHistories histories,
        FtpConnectionFrame frame,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        ref string? error)
    {
        CommandLineState? buffer = focusRow switch
        {
            RowConnectionName => connectionName,
            RowHost => host,
            RowPort => port,
            RowUserName => userName,
            RowPassword => password,
            RowRemoteRoot => remoteRoot,
            RowActivePorts => activePorts,
            _ => null,
        };
        if (buffer is null)
            return false;

        var history = histories.ForRow(focusRow);
        int availableRows = DropdownRows(frame, focusRow, allowTemporaryConnection, dataMode);
        return SingleLineTextInput.HandleKey(buffer, key, ref error, history, availableRows) == TextInputKeyResult.TextChanged;
    }

    private static void ClearCertificateWhenEndpointChanges(
        int focusRow,
        bool changed,
        ref string? certificateFingerprint,
        ref bool trustCertificate)
    {
        if (!changed || focusRow is not (RowHost or RowPort))
            return;

        certificateFingerprint = null;
        trustCertificate = false;
    }

    private static bool TryHandleBodyScrollbarMouse(
        MouseConsoleInputEvent mouse,
        FtpConnectionFrame frame,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        var geometry = frame.Geometry;
        int bodyRowCount = BodyRowCount(allowTemporaryConnection, dataMode);
        if (bodyRowCount <= geometry.BodyBounds.Height)
            return false;

        bodyScrollTop = frame.BodyScrollTop;
        return ScrollBarMouseHandler.TryHandleMouse(
            mouse,
            new Rect(geometry.FrameBounds.Right - 1, geometry.BodyBounds.Y, 1, geometry.BodyBounds.Height),
            bodyRowCount,
            geometry.BodyBounds.Height,
            ref bodyScrollTop,
            ref bodyScrollbarDrag);
    }

    private static bool TryHandleMouse(
        MouseConsoleInputEvent mouse,
        FtpConnectionFrame frame,
        bool allowTemporaryConnection,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        TextFieldHistories histories,
        ref int focusRow,
        ref bool saveConnection,
        ref bool savePassword,
        ref bool showInDrive,
        ref FtpConnectionSecurityMode securityMode,
        ref FtpDataConnectionMode dataMode,
        ref bool useDataTls,
        ref string? certificateFingerprint,
        ref bool trustCertificate,
        ref string? error)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        var geometry = frame.Geometry;
        for (int row = 0; row < FocusRowCount; row++)
        {
            if (row == RowButtons)
                continue;
            if (!IsFocusableRow(row, allowTemporaryConnection, dataMode))
                continue;

            if (BodyY(geometry, row, allowTemporaryConnection, dataMode, frame.BodyScrollTop) is not { } rowY ||
                mouse.Y != rowY ||
                mouse.X < geometry.LabelX ||
                mouse.X >= geometry.ContentBounds.Right - 2)
            {
                continue;
            }

            focusRow = row;
            error = null;
            if (TryGetTextBuffer(row, connectionName, host, port, userName, password, remoteRoot, activePorts) is { } buffer)
            {
                if (row != RowPassword &&
                    SingleLineTextInput.IsHistoryArrowHit(geometry.FieldX, geometry.FieldWidth, rowY, mouse.X, mouse.Y) &&
                    histories.ForRow(row) is { } history)
                {
                    SingleLineTextInput.TryOpenHistoryDropdown(history, rowY, frame.Size.Height);
                    return true;
                }

                if (mouse.X >= geometry.FieldX && mouse.X < geometry.FieldX + geometry.FieldWidth)
                    SetTextCursorFromMouse(buffer, mouse.X, geometry.FieldX, geometry.FieldWidth);
                return true;
            }

            TryToggle(
                row,
                allowTemporaryConnection,
                ref saveConnection,
                ref savePassword,
                ref showInDrive,
                ref securityMode,
                ref dataMode,
                ref useDataTls,
                ref certificateFingerprint,
                ref trustCertificate,
                port);
            focusRow = CoerceFocusableRow(focusRow, allowTemporaryConnection, dataMode);
            return true;
        }

        return false;
    }

    private static bool TryHandleHistoryDropdownMouse(
        MouseConsoleInputEvent mouse,
        FtpConnectionFrame frame,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        TextFieldHistories histories,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        ref ScrollBarDragState? historyScrollbarDrag)
    {
        return TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowConnectionName, connectionName, ref historyScrollbarDrag) ||
            TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowHost, host, ref historyScrollbarDrag) ||
            TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowPort, port, ref historyScrollbarDrag) ||
            TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowUserName, userName, ref historyScrollbarDrag) ||
            TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowRemoteRoot, remoteRoot, ref historyScrollbarDrag) ||
            TryHandleHistoryDropdownRow(mouse, frame, allowTemporaryConnection, dataMode, histories, RowActivePorts, activePorts, ref historyScrollbarDrag);
    }

    private static bool TryHandleHistoryDropdownRow(
        MouseConsoleInputEvent mouse,
        FtpConnectionFrame frame,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        TextFieldHistories histories,
        int row,
        CommandLineState buffer,
        ref ScrollBarDragState? historyScrollbarDrag)
    {
        var geometry = frame.Geometry;
        if (BodyY(geometry, row, allowTemporaryConnection, dataMode, frame.BodyScrollTop) is not { } fieldY ||
            histories.ForRow(row) is not { } history)
        {
            return false;
        }

        return SingleLineTextInput.TryHandleHistoryDropdownMouse(
            history,
            buffer,
            mouse,
            geometry.FieldX,
            fieldY,
            geometry.FieldWidth,
            frame.Size.Height,
            ref historyScrollbarDrag);
    }

    private static CommandLineState? TryGetTextBuffer(
        int focusRow,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts) =>
        focusRow switch
        {
            RowConnectionName => connectionName,
            RowHost => host,
            RowPort => port,
            RowUserName => userName,
            RowPassword => password,
            RowRemoteRoot => remoteRoot,
            RowActivePorts => activePorts,
            _ => null,
        };

    private static void SetTextCursorFromMouse(
        CommandLineState buffer,
        int mouseX,
        int fieldX,
        int fieldWidth)
    {
        int visibleStart = Math.Max(0, buffer.CursorPosition - Math.Max(0, fieldWidth - 1));
        int position = Math.Clamp(visibleStart + mouseX - fieldX, 0, buffer.Text.Length);
        buffer.MoveToStart();
        buffer.MoveCursor(position);
    }

    private DialogButtonBarLayout Draw(
        DialogGeometry geometry,
        string title,
        int focusRow,
        int bodyScrollTop,
        DialogButtonBar buttonBar,
        int focusedButton,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        TextFieldHistories histories,
        bool saveConnection,
        bool savePassword,
        bool showInDrive,
        FtpConnectionSecurityMode securityMode,
        FtpDataConnectionMode dataMode,
        bool useDataTls,
        string? certificateFingerprint,
        bool trustCertificate,
        bool allowTemporaryConnection,
        string? error)
    {
        DialogButtonBarLayout buttons = null!;
        var bounds = geometry.Bounds;
        var fill = FarDialogStyles.Fill;
        var focused = FarDialogStyles.FocusedInput;

        _modalRenderer.Render(_screen, bounds, title, true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect contentBounds = layout.ContentBounds;
            int labelX = geometry.LabelX;
            int fieldX = geometry.FieldX;
            int fieldWidth = geometry.FieldWidth;
            int rowWidth = Math.Max(0, contentBounds.Width - 4);
            _screen.FillRegion(geometry.BodyBounds, fill);

            DrawVisibleTextField(RowConnectionName, "Connection name:", connectionName, mask: false);
            DrawVisibleTextField(RowHost, "Host:", host, mask: false);
            DrawVisibleTextField(RowPort, "Port:", port, mask: false);
            DrawVisibleTextField(RowUserName, "User name:", userName, mask: false);
            DrawVisibleTextField(RowPassword, "Password:", password, mask: true);
            DrawVisibleTextField(RowRemoteRoot, "Remote root:", remoteRoot, mask: false);

            if (allowTemporaryConnection)
                DrawVisibleCheckBox(RowSaveConnection, "Save connection", saveConnection, enabled: true);
            DrawVisibleCheckBox(RowSavePassword, "Save password", savePassword, enabled: true);
            DrawVisibleCheckBox(RowShowInDrive, "Show in drive menu", showInDrive, enabled: true);

            DrawVisibleReadOnly(RowSecurity, "Security:", SecurityLabel(securityMode));
            DrawVisibleReadOnly(RowDataMode, "Data mode:", DataModeLabel(dataMode));
            DrawVisibleCheckBox(RowDataTls, "Use TLS for data connection", useDataTls, enabled: securityMode != FtpConnectionSecurityMode.PlainFtp);
            if (dataMode == FtpDataConnectionMode.Active)
                DrawVisibleTextField(RowActivePorts, "Active ports:", activePorts, mask: false);

            string fingerprintText = securityMode == FtpConnectionSecurityMode.PlainFtp
                ? "(plain FTP has no TLS certificate)"
                : string.IsNullOrWhiteSpace(certificateFingerprint)
                    ? "(press F10 to read certificate)"
                    : certificateFingerprint;
            DrawVisibleReadOnly(RowFingerprint, "TLS cert:", fingerprintText);
            DrawVisibleCheckBox(RowTrustCertificate, "Trust certificate", trustCertificate, enabled: securityMode != FtpConnectionSecurityMode.PlainFtp && !string.IsNullOrWhiteSpace(certificateFingerprint));

            int bodyRowCount = BodyRowCount(allowTemporaryConnection, dataMode);
            if (bodyRowCount > geometry.BodyBounds.Height)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    _screen,
                    new Rect(layout.FrameBounds.Right - 1, geometry.BodyBounds.Y, 1, geometry.BodyBounds.Height),
                    new ScrollState
                    {
                        TotalItems = bodyRowCount,
                        ViewportItems = geometry.BodyBounds.Height,
                        FirstVisibleIndex = bodyScrollTop,
                    },
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    FarDialogStyles.Border);
            }

            int buttonY = contentBounds.Bottom - 1;
            int errorY = buttonY - 1;
            string errorText = error is null ? string.Empty : Truncate(error, rowWidth);
            _screen.Write(labelX, errorY, errorText.PadRight(rowWidth), FarDialogStyles.Error);
            buttons = buttonBar.Render(
                _screen,
                labelX,
                buttonY,
                rowWidth,
                focusedButton,
                fill,
                focusRow == RowButtons ? focused : fill);

            void DrawVisibleTextField(int row, string label, CommandLineState buffer, bool mask)
            {
                if (BodyY(geometry, row, allowTemporaryConnection, dataMode, bodyScrollTop) is { } y)
                    DrawTextField(labelX, fieldX, y, fieldWidth, label, buffer, histories.ForRow(row), focusRow == row, mask);
            }

            void DrawVisibleReadOnly(int row, string label, string value)
            {
                if (BodyY(geometry, row, allowTemporaryConnection, dataMode, bodyScrollTop) is { } y)
                    DrawReadOnly(labelX, fieldX, y, fieldWidth, label, value, focusRow == row);
            }

            void DrawVisibleCheckBox(int row, string label, bool isChecked, bool enabled)
            {
                if (BodyY(geometry, row, allowTemporaryConnection, dataMode, bodyScrollTop) is { } y)
                    DrawCheckBox(labelX, y, rowWidth, label, isChecked, focusRow == row, enabled);
            }
        });

        SetFocusedTextCursor(
            geometry,
            focusRow,
            connectionName,
            host,
            port,
            userName,
            password,
            remoteRoot,
            activePorts,
            histories,
            allowTemporaryConnection,
            dataMode,
            bodyScrollTop);
        return buttons;
    }

    private static DialogGeometry GetDialogGeometry(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(48, size.Width - 2));
        int height = Math.Min(DialogHeight, Math.Max(8, size.Height - 2));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var frameBounds = new Rect(
            bounds.X + 1,
            bounds.Y + 1,
            Math.Max(1, bounds.Width - 2),
            Math.Max(1, bounds.Height - 2));
        var contentBounds = new Rect(
            bounds.X + 2,
            bounds.Y + 2,
            Math.Max(0, bounds.Width - 4),
            Math.Max(0, bounds.Height - 4));
        int buttonY = contentBounds.Bottom - 1;
        int errorY = buttonY - 1;
        var bodyBounds = new Rect(
            contentBounds.X,
            contentBounds.Y,
            contentBounds.Width,
            Math.Max(1, errorY - contentBounds.Y));
        int labelX = contentBounds.X + 2;
        int fieldX = Math.Min(contentBounds.Right - 1, labelX + 24);
        int fieldWidth = Math.Min(FieldWidth, Math.Max(1, contentBounds.Right - fieldX - 1));
        return new DialogGeometry(bounds, frameBounds, contentBounds, bodyBounds, labelX, fieldX, fieldWidth);
    }

    private static int DisplayRow(
        int row,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode)
    {
        int offset = row;
        if (!allowTemporaryConnection && row > RowSaveConnection)
            offset--;
        if (dataMode != FtpDataConnectionMode.Active && row > RowActivePorts)
            offset--;
        return offset;
    }

    private static int BodyRowCount(bool allowTemporaryConnection, FtpDataConnectionMode dataMode) =>
        DisplayRow(RowTrustCertificate, allowTemporaryConnection, dataMode) + 1;

    private static int NormalizeBodyScroll(
        DialogGeometry geometry,
        int focusRow,
        int bodyScrollTop,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode)
    {
        int bodyRowCount = BodyRowCount(allowTemporaryConnection, dataMode);
        int viewportRows = geometry.BodyBounds.Height;
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, bodyRowCount, viewportRows);
        if (focusRow != RowButtons)
        {
            bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(
                DisplayRow(focusRow, allowTemporaryConnection, dataMode),
                bodyScrollTop,
                viewportRows);
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, bodyRowCount, viewportRows);
    }

    private static int ClampBodyScroll(
        DialogGeometry geometry,
        int bodyScrollTop,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode) =>
        ScrollStateCalculator.ClampFirstVisibleIndex(
            bodyScrollTop,
            BodyRowCount(allowTemporaryConnection, dataMode),
            geometry.BodyBounds.Height);

    private static int? BodyY(
        DialogGeometry geometry,
        int row,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        int bodyScrollTop)
    {
        int visibleRow = DisplayRow(row, allowTemporaryConnection, dataMode) - bodyScrollTop;
        return visibleRow >= 0 && visibleRow < geometry.BodyBounds.Height
            ? geometry.BodyBounds.Y + visibleRow
            : null;
    }

    private static int DropdownRows(
        FtpConnectionFrame frame,
        int focusRow,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode)
    {
        int? fieldY = BodyY(frame.Geometry, focusRow, allowTemporaryConnection, dataMode, frame.BodyScrollTop);
        return fieldY is null
            ? 0
            : SingleLineTextInput.AvailableDropdownContentRows(fieldY.Value, frame.Size.Height);
    }

    private void SetFocusedTextCursor(
        DialogGeometry geometry,
        int focusRow,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        TextFieldHistories histories,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        int bodyScrollTop)
    {
        CommandLineState? buffer = focusRow switch
        {
            RowConnectionName => connectionName,
            RowHost => host,
            RowPort => port,
            RowUserName => userName,
            RowPassword => password,
            RowRemoteRoot => remoteRoot,
            RowActivePorts => activePorts,
            _ => null,
        };
        if (buffer is null)
        {
            _screen.SetCursorVisible(false);
            return;
        }

        int fieldX = geometry.FieldX;
        int fieldWidth = geometry.FieldWidth;
        if (BodyY(geometry, focusRow, allowTemporaryConnection, dataMode, bodyScrollTop) is not { } cursorY)
        {
            _screen.SetCursorVisible(false);
            return;
        }

        var history = histories.ForRow(focusRow);
        if (history is not null)
            SingleLineTextInput.RenderHistoryDropdown(_screen, fieldX, cursorY, fieldWidth, history);

        int textWidth = history is null ? fieldWidth : Math.Max(1, fieldWidth - 1);
        int cursorX = SingleLineTextInput.GetCursorX(fieldX, textWidth, buffer);
        if (cursorX >= fieldX + textWidth)
        {
            _screen.SetCursorVisible(false);
            return;
        }

        _screen.SetCursorPosition(cursorX, cursorY);
        _screen.SetCursorVisible(true);
    }

    private readonly record struct DialogGeometry(
        Rect Bounds,
        Rect FrameBounds,
        Rect ContentBounds,
        Rect BodyBounds,
        int LabelX,
        int FieldX,
        int FieldWidth);

    private readonly record struct FtpConnectionFrame(
        ConsoleSize Size,
        DialogGeometry Geometry,
        int BodyScrollTop,
        DialogButtonBarLayout Buttons);

    private sealed class TextFieldHistories
    {
        private readonly SingleLineTextHistoryState _connectionName = HistoryRegistry.GetOrCreate("FtpConnectionDialog.ConnectionName");
        private readonly SingleLineTextHistoryState _host = HistoryRegistry.GetOrCreate("FtpConnectionDialog.Host");
        private readonly SingleLineTextHistoryState _port = HistoryRegistry.GetOrCreate("FtpConnectionDialog.Port");
        private readonly SingleLineTextHistoryState _userName = HistoryRegistry.GetOrCreate("FtpConnectionDialog.UserName");
        private readonly SingleLineTextHistoryState _remoteRoot = HistoryRegistry.GetOrCreate("FtpConnectionDialog.RemoteRoot");
        private readonly SingleLineTextHistoryState _activePorts = HistoryRegistry.GetOrCreate("FtpConnectionDialog.ActivePorts");

        public SingleLineTextHistoryState? ForRow(int row) => row switch
        {
            RowConnectionName => _connectionName,
            RowHost => _host,
            RowPort => _port,
            RowUserName => _userName,
            RowRemoteRoot => _remoteRoot,
            RowActivePorts => _activePorts,
            _ => null,
        };

        public void Add(
            CommandLineState connectionName,
            CommandLineState host,
            CommandLineState port,
            CommandLineState userName,
            CommandLineState remoteRoot,
            CommandLineState activePorts)
        {
            _connectionName.Add(connectionName.Text.Trim());
            _host.Add(host.Text.Trim());
            _port.Add(port.Text.Trim());
            _userName.Add(userName.Text.Trim());
            _remoteRoot.Add(remoteRoot.Text.Trim());
            _activePorts.Add(activePorts.Text.Trim());
        }
    }

    private void DrawTextField(
        int labelX,
        int fieldX,
        int y,
        int fieldWidth,
        string label,
        CommandLineState buffer,
        SingleLineTextHistoryState? history,
        bool focused,
        bool mask)
    {
        var labelStyle = focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill;
        _screen.Write(labelX, y, label.PadRight(Math.Max(0, fieldX - labelX - 1)), labelStyle);

        var fieldStyle = focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input;
        SingleLineTextInput.Render(
            _screen,
            fieldX,
            y,
            fieldWidth,
            buffer,
            fieldStyle,
            FarDialogStyles.Input,
            history,
            maskInput: mask,
            renderDropdown: false);
    }

    private void DrawReadOnly(
        int labelX,
        int fieldX,
        int y,
        int fieldWidth,
        string label,
        string value,
        bool focused)
    {
        var style = focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill;
        _screen.Write(labelX, y, label.PadRight(Math.Max(0, fieldX - labelX - 1)), style);
        _screen.Write(fieldX, y, Truncate(value, fieldWidth).PadRight(fieldWidth), style);
    }

    private void DrawCheckBox(
        int x,
        int y,
        int width,
        string label,
        bool isChecked,
        bool focused,
        bool enabled)
    {
        var style = focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Fill;
        if (!enabled)
            style = FarDialogStyles.Fill;

        string text = $"[{(isChecked ? 'x' : ' ')}] {label}";
        _screen.Write(x, y, Truncate(text, width).PadRight(width), style);
    }

    private static bool TryParseActivePortRange(
        string text,
        FtpDataConnectionMode dataMode,
        out int? from,
        out int? to,
        out string? error)
    {
        from = null;
        to = null;
        error = null;

        if (dataMode != FtpDataConnectionMode.Active || string.IsNullOrWhiteSpace(text))
            return true;

        string[] parts = text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out int singlePort))
        {
            from = singlePort;
            to = singlePort;
        }
        else if (parts.Length == 2 &&
                 int.TryParse(parts[0], out int startPort) &&
                 int.TryParse(parts[1], out int endPort))
        {
            from = startPort;
            to = endPort;
        }
        else
        {
            error = "Active port range must be empty, a port, or start-end.";
            return false;
        }

        if (from is <= 0 or > 65535 ||
            to is <= 0 or > 65535 ||
            from > to)
        {
            error = "Active port range must be between 1 and 65535, with start not greater than end.";
            return false;
        }

        return true;
    }

    private static string? FormatActivePortRange(FtpConnectionInfo? connection)
    {
        if (connection?.ActiveModeLocalPortFrom is not { } from ||
            connection.ActiveModeLocalPortTo is not { } to)
        {
            return null;
        }

        return from == to ? from.ToString() : $"{from}-{to}";
    }

    private static FtpConnectionSecurityMode NextSecurityMode(FtpConnectionSecurityMode mode) =>
        mode switch
        {
            FtpConnectionSecurityMode.ExplicitFtps => FtpConnectionSecurityMode.ImplicitFtps,
            FtpConnectionSecurityMode.ImplicitFtps => FtpConnectionSecurityMode.Auto,
            FtpConnectionSecurityMode.Auto => FtpConnectionSecurityMode.PlainFtp,
            _ => FtpConnectionSecurityMode.ExplicitFtps,
        };

    private static int DefaultPort(FtpConnectionSecurityMode mode) =>
        mode == FtpConnectionSecurityMode.ImplicitFtps ? 990 : 21;

    private static string SecurityLabel(FtpConnectionSecurityMode mode) =>
        mode switch
        {
            FtpConnectionSecurityMode.PlainFtp => "Plain FTP - no TLS",
            FtpConnectionSecurityMode.ExplicitFtps => "Explicit FTPS",
            FtpConnectionSecurityMode.ImplicitFtps => "Implicit FTPS",
            FtpConnectionSecurityMode.Auto => "Auto FTP/FTPS",
            _ => mode.ToString(),
        };

    private static string DataModeLabel(FtpDataConnectionMode mode) =>
        mode switch
        {
            FtpDataConnectionMode.AutoPassive => "Auto passive",
            FtpDataConnectionMode.Passive => "Passive",
            FtpDataConnectionMode.Active => "Active",
            _ => mode.ToString(),
        };

    private static string Truncate(string text, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }
}
