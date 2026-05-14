using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

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
    public static SftpConnectionDialogValidationResult Accepted() =>
        new(true, null, null);

    public static SftpConnectionDialogValidationResult Error(string message) =>
        new(false, message, null);

    public static SftpConnectionDialogValidationResult RequireHostKeyTrust(string fingerprint) =>
        new(false, "Review the host key fingerprint and check Trust host key.", fingerprint);
}

internal sealed class SftpConnectionDialog
{
    private const int DialogWidth = 74;
    private const int DialogHeight = 18;
    private const int FieldWidth = 42;

    private const int RowConnectionName = 0;
    private const int RowHost = 1;
    private const int RowPort = 2;
    private const int RowUserName = 3;
    private const int RowPassword = 4;
    private const int RowRemoteRoot = 5;
    private const int RowSaveConnection = 6;
    private const int RowSavePassword = 7;
    private const int RowShowInDrive = 8;
    private const int RowFingerprint = 9;
    private const int RowTrustHostKey = 10;
    private const int RowButtons = 11;
    private const int FocusRowCount = 12;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;

    public SftpConnectionDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public SftpConnectionDialogResult? Show(
        SftpConnectionDialogRequest request,
        Func<SftpConnectionDialogResult, SftpConnectionDialogValidationResult> validate)
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

    private SftpConnectionDialogResult? RunLoop(
        SftpConnectionDialogRequest request,
        Func<SftpConnectionDialogResult, SftpConnectionDialogValidationResult> validate,
        ConsoleSize size)
    {
        var connection = request.Connection;
        var connectionName = TextBuffer(connection?.DisplayName ?? string.Empty);
        var host = TextBuffer(connection?.Host ?? string.Empty);
        var port = TextBuffer((connection?.Port ?? 22).ToString());
        var userName = TextBuffer(connection?.Username ?? string.Empty);
        var password = TextBuffer(request.SavedPassword ?? string.Empty);
        var remoteRoot = TextBuffer(connection?.RemoteRootPath ?? "/");

        bool saveConnection = request.SaveConnectionByDefault;
        bool savePassword = connection?.CredentialId is not null && request.SavedPassword is not null;
        bool showInDrive = connection?.ShowInDriveSelection ?? true;
        string? hostKeyFingerprint = connection?.ExpectedHostKeyFingerprint;
        bool trustHostKey = !string.IsNullOrWhiteSpace(hostKeyFingerprint);
        string? error = null;
        int focusRow = RowConnectionName;
        int focusedButton = 0;
        string submitLabel = request.AllowTemporaryConnection ? "Connect" : "Save";
        var buttonBar = new DialogButtonBar(
        [
            new DialogButton("submit", submitLabel, submitLabel[0], IsDefault: true),
            new DialogButton("cancel", "Cancel", 'C'),
        ]);

        while (true)
        {
            Draw(
                size,
                connection is null ? "SFTP connection" : "Edit SFTP connection",
                focusRow,
                buttonBar,
                focusedButton,
                connectionName,
                host,
                port,
                userName,
                password,
                remoteRoot,
                saveConnection,
                savePassword,
                showInDrive,
                hostKeyFingerprint,
                trustHostKey,
                request.AllowTemporaryConnection,
                error);

            var input = _screen.ReadInput();
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
                    else if (TryToggle(focusRow, request.AllowTemporaryConnection, hostKeyFingerprint, ref saveConnection, ref savePassword, ref showInDrive, ref trustHostKey))
                    {
                        error = null;
                    }
                    break;
                case ConsoleKey.UpArrow:
                    focusRow = PreviousRow(focusRow, request.AllowTemporaryConnection);
                    error = null;
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.Tab:
                    focusRow = NextRow(focusRow, request.AllowTemporaryConnection);
                    error = null;
                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                    if (focusRow == RowButtons)
                        buttonBar.TryHandleInput(input, ref focusedButton, out _);
                    else
                        ClearHostKeyWhenEndpointChanges(
                            focusRow,
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, ref error),
                            ref hostKeyFingerprint,
                            ref trustHostKey);
                    break;
                case ConsoleKey.Spacebar:
                    if (focusRow == RowButtons)
                    {
                        if (focusedButton == 1)
                            return null;
                        if (TrySubmit(out var spaceResult))
                            return spaceResult;
                    }
                    else if (TryToggle(focusRow, request.AllowTemporaryConnection, hostKeyFingerprint, ref saveConnection, ref savePassword, ref showInDrive, ref trustHostKey))
                    {
                        error = null;
                    }
                    else
                    {
                        ClearHostKeyWhenEndpointChanges(
                            focusRow,
                            EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, ref error),
                            ref hostKeyFingerprint,
                            ref trustHostKey);
                    }
                    break;
                default:
                    ClearHostKeyWhenEndpointChanges(
                        focusRow,
                        EditFocusedText(focusRow, key, connectionName, host, port, userName, password, remoteRoot, ref error),
                        ref hostKeyFingerprint,
                        ref trustHostKey);
                    break;
            }
        }

        bool TrySubmit(out SftpConnectionDialogResult? result)
        {
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
                trustHostKey ? hostKeyFingerprint : null);
            if (result is null)
            {
                error = "Host, user name, password, and remote root are required.";
                return false;
            }

            var validation = validate(result);
            if (validation.HostKeyFingerprint is not null)
            {
                hostKeyFingerprint = validation.HostKeyFingerprint;
                trustHostKey = false;
                focusRow = RowTrustHostKey;
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

    private static SftpConnectionDialogResult? BuildResult(
        SftpConnectionDialogRequest request,
        string connectionName,
        string host,
        string portText,
        string userName,
        string password,
        string remoteRoot,
        bool saveConnection,
        bool savePassword,
        bool showInDrive,
        string? hostKeyFingerprint)
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
            ? request.Connection?.CredentialId ?? connectionId
            : null;

        var connection = new SftpConnectionInfo
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
        };

        return new SftpConnectionDialogResult(
            connection,
            password,
            saveConnection,
            savePassword,
            request.Connection?.CredentialId);
    }

    private static int NextRow(int focusRow, bool allowTemporaryConnection)
    {
        do
        {
            focusRow = (focusRow + 1) % FocusRowCount;
        } while (!IsFocusableRow(focusRow, allowTemporaryConnection));

        return focusRow;
    }

    private static int PreviousRow(int focusRow, bool allowTemporaryConnection)
    {
        do
        {
            focusRow = focusRow == 0 ? FocusRowCount - 1 : focusRow - 1;
        } while (!IsFocusableRow(focusRow, allowTemporaryConnection));

        return focusRow;
    }

    private static bool IsFocusableRow(int focusRow, bool allowTemporaryConnection) =>
        focusRow != RowSaveConnection || allowTemporaryConnection;

    private static bool TryToggle(
        int focusRow,
        bool allowTemporaryConnection,
        string? hostKeyFingerprint,
        ref bool saveConnection,
        ref bool savePassword,
        ref bool showInDrive,
        ref bool trustHostKey)
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
            case RowTrustHostKey:
                if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
                    trustHostKey = !trustHostKey;
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
            _ => null,
        };
        if (buffer is null)
            return false;

        return SingleLineTextInput.HandleKey(buffer, key, ref error) == TextInputKeyResult.TextChanged;
    }

    private static void ClearHostKeyWhenEndpointChanges(
        int focusRow,
        bool changed,
        ref string? hostKeyFingerprint,
        ref bool trustHostKey)
    {
        if (!changed || focusRow is not (RowHost or RowPort))
            return;

        hostKeyFingerprint = null;
        trustHostKey = false;
    }

    private void Draw(
        ConsoleSize size,
        string title,
        int focusRow,
        DialogButtonBar buttonBar,
        int focusedButton,
        CommandLineState connectionName,
        CommandLineState host,
        CommandLineState port,
        CommandLineState userName,
        CommandLineState password,
        CommandLineState remoteRoot,
        bool saveConnection,
        bool savePassword,
        bool showInDrive,
        string? hostKeyFingerprint,
        bool trustHostKey,
        bool allowTemporaryConnection,
        string? error)
    {
        using var frame = _screen.BeginFrame();

        int width = Math.Min(DialogWidth, Math.Max(42, size.Width - 2));
        int height = Math.Min(DialogHeight, Math.Max(14, size.Height - 2));
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);

        new DialogFrameRenderer().RenderFrame(_screen, bounds, title, true, PaletteStyles.DialogPopupOptions(_palette), (_, contentBounds) =>
        {
            int labelX = contentBounds.X + 2;
            int fieldX = Math.Min(contentBounds.Right - 1, labelX + 22);
            int fieldWidth = Math.Min(FieldWidth, Math.Max(1, contentBounds.Right - fieldX - 1));

            DrawTextField(labelX, fieldX, contentBounds.Y + 0, fieldWidth, "Connection name:", connectionName, focusRow == RowConnectionName, mask: false);
            DrawTextField(labelX, fieldX, contentBounds.Y + 1, fieldWidth, "Host:", host, focusRow == RowHost, mask: false);
            DrawTextField(labelX, fieldX, contentBounds.Y + 2, fieldWidth, "Port:", port, focusRow == RowPort, mask: false);
            DrawTextField(labelX, fieldX, contentBounds.Y + 3, fieldWidth, "User name:", userName, focusRow == RowUserName, mask: false);
            DrawTextField(labelX, fieldX, contentBounds.Y + 4, fieldWidth, "Password:", password, focusRow == RowPassword, mask: true);
            DrawTextField(labelX, fieldX, contentBounds.Y + 5, fieldWidth, "Remote root:", remoteRoot, focusRow == RowRemoteRoot, mask: false);

            if (allowTemporaryConnection)
                DrawCheckBox(labelX, DisplayY(contentBounds, RowSaveConnection), contentBounds.Width - 4, "Save connection", saveConnection, focusRow == RowSaveConnection, enabled: true);
            DrawCheckBox(labelX, DisplayY(contentBounds, RowSavePassword), contentBounds.Width - 4, "Save password", savePassword, focusRow == RowSavePassword, enabled: true);
            DrawCheckBox(labelX, DisplayY(contentBounds, RowShowInDrive), contentBounds.Width - 4, "Show in drive menu", showInDrive, focusRow == RowShowInDrive, enabled: true);

            string fingerprintText = string.IsNullOrWhiteSpace(hostKeyFingerprint)
                ? "(press F10 to read host key)"
                : hostKeyFingerprint;
            DrawReadOnly(labelX, fieldX, DisplayY(contentBounds, RowFingerprint), fieldWidth, "Host key:", fingerprintText, focusRow == RowFingerprint);
            DrawCheckBox(labelX, DisplayY(contentBounds, RowTrustHostKey), contentBounds.Width - 4, "Trust host key", trustHostKey, focusRow == RowTrustHostKey, enabled: !string.IsNullOrWhiteSpace(hostKeyFingerprint));

            int buttonY = contentBounds.Bottom - 1;
            int errorY = buttonY - 1;
            string errorText = error is null ? string.Empty : Truncate(error, contentBounds.Width - 4);
            _screen.Write(labelX, errorY, errorText.PadRight(Math.Max(0, contentBounds.Width - 4)), PaletteStyles.DialogError(_palette));
            buttonBar.Render(
                _screen,
                labelX,
                buttonY,
                Math.Max(0, contentBounds.Width - 4),
                focusedButton,
                PaletteStyles.DialogFill(_palette),
                focusRow == RowButtons ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette));
        });

        _screen.SetCursorVisible(false);

        int DisplayY(Rect contentBounds, int row)
        {
            int offset = row;
            if (!allowTemporaryConnection && row > RowSaveConnection)
                offset--;
            return contentBounds.Y + offset;
        }
    }

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
        var labelStyle = focused ? PaletteStyles.InputHighlight(_palette) : PaletteStyles.DialogFill(_palette);
        _screen.Write(labelX, y, label.PadRight(Math.Max(0, fieldX - labelX - 1)), labelStyle);

        var fieldStyle = focused ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
        SingleLineTextInput.Render(
            _screen,
            fieldX,
            y,
            fieldWidth,
            buffer,
            fieldStyle,
            PaletteStyles.InputHighlight(_palette),
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
        var style = focused ? PaletteStyles.InputHighlight(_palette) : PaletteStyles.DialogFill(_palette);
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
        var style = focused ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
        if (!enabled)
            style = PaletteStyles.DialogFill(_palette);

        string text = $"[{(isChecked ? 'x' : ' ')}] {label}";
        _screen.Write(x, y, Truncate(text, width).PadRight(width), style);
    }

    private static string Truncate(string text, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }
}
