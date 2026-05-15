using CSharpFar.Ui;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Sftp;

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
    private readonly ModalDialogRenderer _modalRenderer = new();

    public SftpConnectionDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _ = palette;
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
                ? NormalizeBodyScroll(size, focusRow, bodyScrollTop, request.AllowTemporaryConnection)
                : ScrollStateCalculator.ClampFirstVisibleIndex(
                    bodyScrollTop,
                    BodyRowCount(request.AllowTemporaryConnection),
                    BodyViewportRows(size));
            ensureFocusVisible = true;

            Draw(
                size,
                connection is null ? "SFTP connection" : "Edit SFTP connection",
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
                saveConnection,
                savePassword,
                showInDrive,
                hostKeyFingerprint,
                trustHostKey,
                request.AllowTemporaryConnection,
                error);

            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent scrollbarMouse &&
                TryHandleBodyScrollbarMouse(
                    scrollbarMouse,
                    size,
                    request.AllowTemporaryConnection,
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
                    bodyScrollTop,
                    ref focusRow,
                    ref saveConnection,
                    ref savePassword,
                    ref showInDrive,
                    ref trustHostKey,
                    ref error,
                    hostKeyFingerprint);
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
                    focusRow = NextRow(focusRow, request.AllowTemporaryConnection);
                    error = null;
                    break;
                case ConsoleKey.Tab:
                    focusRow = IsShiftTab(key)
                        ? PreviousRow(focusRow, request.AllowTemporaryConnection)
                        : NextRow(focusRow, request.AllowTemporaryConnection);
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

    private static bool IsShiftTab(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Tab &&
        (key.Modifiers & ConsoleModifiers.Shift) != 0;

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

    private static bool TryHandleBodyScrollbarMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        bool allowTemporaryConnection,
        ref int bodyScrollTop,
        ref ScrollBarDragState? bodyScrollbarDrag)
    {
        var geometry = GetDialogGeometry(size);
        int bodyRowCount = BodyRowCount(allowTemporaryConnection);
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
        int bodyScrollTop,
        ref int focusRow,
        ref bool saveConnection,
        ref bool savePassword,
        ref bool showInDrive,
        ref bool trustHostKey,
        ref string? error,
        string? hostKeyFingerprint)
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
            if (!IsFocusableRow(row, allowTemporaryConnection))
                continue;

            if (BodyY(geometry, row, allowTemporaryConnection, bodyScrollTop) is not { } rowY ||
                mouse.Y != rowY ||
                mouse.X < geometry.LabelX ||
                mouse.X >= geometry.ContentBounds.Right - 2)
            {
                continue;
            }

            focusRow = row;
            error = null;
            if (TryGetTextBuffer(row, connectionName, host, port, userName, password, remoteRoot) is { } buffer)
            {
                if (mouse.X >= geometry.FieldX && mouse.X < geometry.FieldX + geometry.FieldWidth)
                    SetTextCursorFromMouse(buffer, mouse.X, geometry.FieldX, geometry.FieldWidth);
                return true;
            }

            TryToggle(
                row,
                allowTemporaryConnection,
                hostKeyFingerprint,
                ref saveConnection,
                ref savePassword,
                ref showInDrive,
                ref trustHostKey);
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
        CommandLineState remoteRoot) =>
        focusRow switch
        {
            RowConnectionName => connectionName,
            RowHost => host,
            RowPort => port,
            RowUserName => userName,
            RowPassword => password,
            RowRemoteRoot => remoteRoot,
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
        bool saveConnection,
        bool savePassword,
        bool showInDrive,
        string? hostKeyFingerprint,
        bool trustHostKey,
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

            string fingerprintText = string.IsNullOrWhiteSpace(hostKeyFingerprint)
                ? "(press F10 to read host key)"
                : hostKeyFingerprint;
            DrawVisibleReadOnly(RowFingerprint, "Host key:", fingerprintText);
            DrawVisibleCheckBox(RowTrustHostKey, "Trust host key", trustHostKey, enabled: !string.IsNullOrWhiteSpace(hostKeyFingerprint));

            int bodyRowCount = BodyRowCount(allowTemporaryConnection);
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
                if (BodyY(geometry, row, allowTemporaryConnection, bodyScrollTop) is { } y)
                    DrawTextField(labelX, fieldX, y, fieldWidth, label, buffer, focusRow == row, mask);
            }

            void DrawVisibleReadOnly(int row, string label, string value)
            {
                if (BodyY(geometry, row, allowTemporaryConnection, bodyScrollTop) is { } y)
                    DrawReadOnly(labelX, fieldX, y, fieldWidth, label, value, focusRow == row);
            }

            void DrawVisibleCheckBox(int row, string label, bool isChecked, bool enabled)
            {
                if (BodyY(geometry, row, allowTemporaryConnection, bodyScrollTop) is { } y)
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
            allowTemporaryConnection,
            bodyScrollTop);
    }

    private static DialogGeometry GetDialogGeometry(ConsoleSize size)
    {
        int width = Math.Min(DialogWidth, Math.Max(42, size.Width - 2));
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
        int fieldX = Math.Min(contentBounds.Right - 1, labelX + 22);
        int fieldWidth = Math.Min(FieldWidth, Math.Max(1, contentBounds.Right - fieldX - 1));
        return new DialogGeometry(bounds, frameBounds, contentBounds, bodyBounds, labelX, fieldX, fieldWidth);
    }

    private static int DisplayRow(int row, bool allowTemporaryConnection)
    {
        int offset = row;
        if (!allowTemporaryConnection && row > RowSaveConnection)
            offset--;
        return offset;
    }

    private static int BodyRowCount(bool allowTemporaryConnection) =>
        DisplayRow(RowTrustHostKey, allowTemporaryConnection) + 1;

    private static int BodyViewportRows(ConsoleSize size) =>
        GetDialogGeometry(size).BodyBounds.Height;

    private static int NormalizeBodyScroll(
        ConsoleSize size,
        int focusRow,
        int bodyScrollTop,
        bool allowTemporaryConnection)
    {
        int bodyRowCount = BodyRowCount(allowTemporaryConnection);
        int viewportRows = BodyViewportRows(size);
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, bodyRowCount, viewportRows);
        if (focusRow != RowButtons)
        {
            bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(
                DisplayRow(focusRow, allowTemporaryConnection),
                bodyScrollTop,
                viewportRows);
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(bodyScrollTop, bodyRowCount, viewportRows);
    }

    private static int? BodyY(
        DialogGeometry geometry,
        int row,
        bool allowTemporaryConnection,
        int bodyScrollTop)
    {
        int visibleRow = DisplayRow(row, allowTemporaryConnection) - bodyScrollTop;
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
        bool allowTemporaryConnection,
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
            _ => null,
        };
        if (buffer is null)
        {
            _screen.SetCursorVisible(false);
            return;
        }

        int fieldX = geometry.FieldX;
        int fieldWidth = geometry.FieldWidth;
        if (BodyY(geometry, focusRow, allowTemporaryConnection, bodyScrollTop) is not { } cursorY)
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

    private static string Truncate(string text, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }
}
