using CSharpFar.Ui;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Ftp;

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

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FtpConnectionDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public FtpConnectionDialogResult? Show(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(validate);

        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(request, validate, size);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private FtpConnectionDialogResult? RunLoop(
        FtpConnectionDialogRequest request,
        Func<FtpConnectionDialogResult, FtpConnectionDialogValidationResult> validate,
        ConsoleSize size)
    {
        var connection = request.Connection;
        var connectionName = TextBuffer(connection?.DisplayName ?? string.Empty);
        var host = TextBuffer(connection?.Host ?? string.Empty);
        var port = TextBuffer((connection?.Port ?? 21).ToString());
        var userName = TextBuffer(connection?.Username ?? string.Empty);
        var password = TextBuffer(request.SavedPassword ?? string.Empty);
        var remoteRoot = TextBuffer(connection?.RemoteRootPath ?? "/");
        var activePorts = TextBuffer(FormatActivePortRange(connection) ?? string.Empty);

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
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("submit", submitLabel, submitLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

        while (true)
        {
            bodyScrollTop = ensureFocusVisible
                ? NormalizeBodyScroll(size, focusRow, bodyScrollTop, request.AllowTemporaryConnection, dataMode)
                : ScrollStateCalculator.ClampFirstVisibleIndex(
                    bodyScrollTop,
                    BodyRowCount(request.AllowTemporaryConnection, dataMode),
                    BodyViewportRows(size));
            ensureFocusVisible = true;

            Draw(
                size,
                connection is null ? "FTP/FTPS connection" : "Edit FTP/FTPS connection",
                focusRow,
                bodyScrollTop,
                buttonBar,
                focusedButton,
                connectionName,
                host,
                port,
                userName,
                password,
                remoteRoot,
                activePorts,
                saveConnection,
                savePassword,
                showInDrive,
                securityMode,
                dataMode,
                useDataTls,
                certificateFingerprint,
                trustCertificate,
                request.AllowTemporaryConnection,
                error);

            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent scrollbarMouse &&
                TryHandleBodyScrollbarMouse(
                    scrollbarMouse,
                    size,
                    request.AllowTemporaryConnection,
                    dataMode,
                    ref bodyScrollTop,
                    ref bodyScrollbarDrag))
            {
                ensureFocusVisible = false;
                continue;
            }

            if ((focusRow == RowButtons || input is MouseConsoleInputEvent) &&
                buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
            {
                focusRow = RowButtons;
                if (buttonId == "cancel")
                    return null;
                if (buttonId == "submit" && TrySubmit(out var submitResult))
                    return submitResult;
                continue;
            }

            if (input is MouseConsoleInputEvent mouse)
            {
                TryHandleMouse(
                    mouse,
                    size,
                    request.AllowTemporaryConnection,
                    connectionName,
                    host,
                    port,
                    userName,
                    password,
                    remoteRoot,
                    activePorts,
                    bodyScrollTop,
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
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    return null;
                case ConsoleKey.F10:
                    if (TrySubmit(out var f10Result))
                        return f10Result;
                    break;
                case ConsoleKey.Enter:
                    if (focusRow == RowButtons)
                    {
                        if (focusedButton == 1)
                            return null;
                        if (TrySubmit(out var enterResult))
                            return enterResult;
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
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        ClearCertificateWhenEndpointChanges(
                            focusRow,
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, ref error),
                            ref certificateFingerprint,
                            ref trustCertificate);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == RowButtons)
                    {
                        if (focusedButton == 1)
                            return null;
                        if (TrySubmit(out var spaceResult))
                            return spaceResult;
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
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, ref error),
                            ref certificateFingerprint,
                            ref trustCertificate);
                    }
                    break;
                default:
                    ClearCertificateWhenEndpointChanges(
                        focusRow,
                        EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, activePorts, ref error),
                        ref certificateFingerprint,
                        ref trustCertificate);
                    break;
            }
        }

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
                return true;

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

        return SingleLineTextInput.HandleKey(buffer, key, ref error) == TextInputKeyResult.TextChanged;
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
        ConsoleSize size,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        var geometry = GetDialogGeometry(size);
        int bodyRowCount = BodyRowCount(allowTemporaryConnection, dataMode);
        if (bodyRowCount <= geometry.BodyBounds.Height)
            return false;

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
        ConsoleSize size,
        bool allowTemporaryConnection,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        CommandLineState activePorts,
        int bodyScrollTop,
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

        var geometry = GetDialogGeometry(size);
        for (int row = 0; row < FocusRowCount; row++)
        {
            if (row == RowButtons)
                continue;
            if (!IsFocusableRow(row, allowTemporaryConnection, dataMode))
                continue;

            if (BodyY(geometry, row, allowTemporaryConnection, dataMode, bodyScrollTop) is not { } rowY ||
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

    private void Draw(
        ConsoleSize size,
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
        using var frame = _screen.BeginFrame();

        var geometry = GetDialogGeometry(size);
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
            buttonBar.Render(
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
                    DrawTextField(labelX, fieldX, y, fieldWidth, label, buffer, focusRow == row, mask);
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
            allowTemporaryConnection,
            dataMode,
            bodyScrollTop);
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

    private static int BodyViewportRows(ConsoleSize size) =>
        GetDialogGeometry(size).BodyBounds.Height;

    private static int NormalizeBodyScroll(
        ConsoleSize size,
        int focusRow,
        int bodyScrollTop,
        bool allowTemporaryConnection,
        FtpDataConnectionMode dataMode)
    {
        int bodyRowCount = BodyRowCount(allowTemporaryConnection, dataMode);
        int viewportRows = BodyViewportRows(size);
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

        int cursorX = SingleLineTextInput.GetCursorX(fieldX, fieldWidth, buffer);
        if (cursorX >= fieldX + fieldWidth)
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

    private void DrawTextField(
        int labelX,
        int fieldX,
        int y,
        int fieldWidth,
        string label,
        CommandLineState buffer,
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
            mask);
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
