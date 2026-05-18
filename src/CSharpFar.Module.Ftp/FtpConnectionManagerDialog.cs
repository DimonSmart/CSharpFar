using CSharpFar.Ui;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Module.Ftp;

internal enum FtpConnectionManagerAction
{
    Connect,
    Create,
    Edit,
    Delete,
}

internal sealed record FtpConnectionManagerResult(
    FtpConnectionManagerAction Action,
    FtpConnectionInfo? Connection);

internal sealed class FtpConnectionManagerDialog
{
    private const int DialogWidth = 76;
    private const int MaxVisibleRows = 12;

    private readonly ScreenRenderer _screen;
    private readonly ModalDialogRenderer _modalRenderer = new();

    public FtpConnectionManagerDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _ = palette;
    }

    public FtpConnectionManagerResult? Show(IReadOnlyList<FtpConnectionInfo> connections)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        _screen.SetCursorVisible(false);

        try
        {
            return RunLoop(connections, size);
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private FtpConnectionManagerResult? RunLoop(IReadOnlyList<FtpConnectionInfo> connections, ConsoleSize size)
    {
        int cursor = 0;
        int scrollTop = 0;
        bool focusButtons = connections.Count == 0;
        int focusedButton = 0;
        ScrollBarDragState? scrollbarDrag = null;
        var buttonBar = CreateButtonBar(hasConnections: connections.Count > 0);

        while (true)
        {
            var geometry = GetDialogGeometry(size, connections.Count);
            cursor = Math.Clamp(cursor, 0, Math.Max(0, connections.Count - 1));
            scrollTop = NormalizeListScroll(connections.Count, geometry.ListBounds.Height, cursor, scrollTop);
            focusedButton = Math.Clamp(focusedButton, 0, buttonBar.Count - 1);
            Draw(connections, size, cursor, scrollTop, focusButtons, buttonBar, focusedButton);

            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent scrollbarMouse &&
                TryHandleScrollbarMouse(scrollbarMouse, size, connections.Count, ref cursor, ref scrollTop, ref scrollbarDrag))
            {
                focusButtons = false;
                continue;
            }

            if (input is MouseConsoleInputEvent mouse &&
                buttonBar.TryHandleInput(mouse, ref focusedButton, out string? mouseButtonId))
            {
                focusButtons = true;
                if (mouseButtonId is not null &&
                    TryCreateButtonResult(mouseButtonId, connections, cursor, out var mouseResult))
                {
                    return mouseResult;
                }

                continue;
            }

            if (input is MouseConsoleInputEvent listMouse &&
                TryHandleListMouse(listMouse, size, connections.Count, scrollTop, ref cursor, out bool connect))
            {
                focusButtons = false;
                if (connect && TrySelected(connections, cursor, out var connectConnection))
                    return new FtpConnectionManagerResult(FtpConnectionManagerAction.Connect, connectConnection);
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            if (focusButtons &&
                buttonBar.TryHandleInput(input, ref focusedButton, out string? keyButtonId))
            {
                if (keyButtonId is not null &&
                    TryCreateButtonResult(keyButtonId, connections, cursor, out var keyResult))
                {
                    return keyResult;
                }

                continue;
            }

            int visibleRows = geometry.ListBounds.Height;
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return null;
                case ConsoleKey.Tab:
                    focusButtons = connections.Count == 0 || !focusButtons;
                    break;
                case ConsoleKey.Insert:
                case ConsoleKey.N:
                    return new FtpConnectionManagerResult(FtpConnectionManagerAction.Create, null);
                case ConsoleKey.F4:
                case ConsoleKey.E:
                    if (TrySelected(connections, cursor, out var editConnection))
                        return new FtpConnectionManagerResult(FtpConnectionManagerAction.Edit, editConnection);
                    break;
                case ConsoleKey.Delete:
                case ConsoleKey.D:
                    if (TrySelected(connections, cursor, out var deleteConnection))
                        return new FtpConnectionManagerResult(FtpConnectionManagerAction.Delete, deleteConnection);
                    break;
                case ConsoleKey.Enter:
                    if (TrySelected(connections, cursor, out var connectConnection))
                        return new FtpConnectionManagerResult(FtpConnectionManagerAction.Connect, connectConnection);
                    break;
                case ConsoleKey.UpArrow:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = Math.Max(0, cursor - 1);
                    break;
                case ConsoleKey.DownArrow:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = Math.Min(Math.Max(0, connections.Count - 1), cursor + 1);
                    break;
                case ConsoleKey.PageUp:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = Math.Max(0, cursor - visibleRows);
                    break;
                case ConsoleKey.PageDown:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = Math.Min(Math.Max(0, connections.Count - 1), cursor + visibleRows);
                    break;
                case ConsoleKey.Home:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = 0;
                    break;
                case ConsoleKey.End:
                    if (connections.Count == 0)
                    {
                        focusButtons = true;
                        break;
                    }
                    focusButtons = false;
                    cursor = Math.Max(0, connections.Count - 1);
                    break;
            }
        }
    }

    private void Draw(
        IReadOnlyList<FtpConnectionInfo> connections,
        ConsoleSize size,
        int cursor,
        int scrollTop,
        bool focusButtons,
        DialogButtonBar buttonBar,
        int focusedButton)
    {
        using var frame = _screen.BeginFrame();

        var geometry = GetDialogGeometry(size, connections.Count);
        var fill = FarDialogStyles.Fill;
        var focused = FarDialogStyles.FocusedInput;
        var scrollState = connections.Count > geometry.ListBounds.Height
            ? new ScrollState
            {
                TotalItems = connections.Count,
                ViewportItems = geometry.ListBounds.Height,
                FirstVisibleIndex = scrollTop,
            }
            : null;

        _modalRenderer.Render(_screen, geometry.Bounds, "FTP/FTPS connections", true, FarDialogStyles.OuterOptions, FarDialogStyles.FrameOptions, (_, layout) =>
        {
            if (scrollState is not null)
            {
                new ScrollBarRenderer().RenderVerticalScrollbar(
                    _screen,
                    new Rect(layout.FrameBounds.Right - 1, geometry.ListBounds.Y, 1, geometry.ListBounds.Height),
                    scrollState,
                    new ScrollBarOptions
                    {
                        Enabled = true,
                        DrawWhenNotScrollable = false,
                    },
                    FarDialogStyles.Border);
            }

            _screen.FillRegion(geometry.ListBounds, fill);
            if (connections.Count == 0)
            {
                _screen.Write(
                    geometry.ListBounds.X,
                    geometry.ListBounds.Y,
                    "No saved FTP/FTPS connections.".PadRight(geometry.ListBounds.Width),
                    fill);
            }
            else
            {
                for (int row = 0; row < geometry.ListBounds.Height; row++)
                {
                    int index = scrollTop + row;
                    if (index >= connections.Count)
                        break;

                    var connection = connections[index];
                    string marker = connection.ShowInDriveSelection ? "*" : " ";
                    string text = $"{marker} {SecurityLabel(connection.SecurityMode)} {connection.DisplayName}  {connection.Username}@{connection.Host}:{connection.Port}";
                    var style = index == cursor ? focused : fill;
                    _screen.Write(
                        geometry.ListBounds.X,
                        geometry.ListBounds.Y + row,
                        Truncate(text, geometry.ListBounds.Width).PadRight(geometry.ListBounds.Width),
                        style);
                }
            }

            buttonBar.Render(
                _screen,
                geometry.ListBounds.X,
                geometry.ButtonY,
                geometry.ListBounds.Width,
                focusedButton,
                fill,
                focusButtons ? focused : fill);
        });

        _screen.SetCursorVisible(false);
    }

    private static DialogButtonBar CreateButtonBar(bool hasConnections) =>
        hasConnections
            ? new DialogButtonBar(
            [
                new DialogButton("connect", "Connect", 'O', IsDefault: true),
                new DialogButton("create", "New", 'N'),
                new DialogButton("edit", "Edit", 'E'),
                new DialogButton("delete", "Delete", 'D'),
                new DialogButton("cancel", "Cancel", 'C'),
            ])
            : new DialogButtonBar(
            [
                new DialogButton("create", "New", 'N', IsDefault: true),
                new DialogButton("cancel", "Cancel", 'C'),
            ]);

    private static bool TryCreateButtonResult(
        string buttonId,
        IReadOnlyList<FtpConnectionInfo> connections,
        int cursor,
        out FtpConnectionManagerResult? result)
    {
        result = null;
        switch (buttonId)
        {
            case "cancel":
                return true;
            case "create":
                result = new FtpConnectionManagerResult(FtpConnectionManagerAction.Create, null);
                return true;
            case "connect":
                if (TrySelected(connections, cursor, out var connectConnection))
                {
                    result = new FtpConnectionManagerResult(FtpConnectionManagerAction.Connect, connectConnection);
                    return true;
                }
                return false;
            case "edit":
                if (TrySelected(connections, cursor, out var editConnection))
                {
                    result = new FtpConnectionManagerResult(FtpConnectionManagerAction.Edit, editConnection);
                    return true;
                }
                return false;
            case "delete":
                if (TrySelected(connections, cursor, out var deleteConnection))
                {
                    result = new FtpConnectionManagerResult(FtpConnectionManagerAction.Delete, deleteConnection);
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static bool TrySelected(
        IReadOnlyList<FtpConnectionInfo> connections,
        int cursor,
        out FtpConnectionInfo connection)
    {
        if (cursor >= 0 && cursor < connections.Count)
        {
            connection = connections[cursor];
            return true;
        }

        connection = null!;
        return false;
    }

    private static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int itemCount,
        ref int cursor,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        var geometry = GetDialogGeometry(size, itemCount);
        if (itemCount <= geometry.ListBounds.Height)
            return false;

        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            new Rect(geometry.FrameBounds.Right - 1, geometry.ListBounds.Y, 1, geometry.ListBounds.Height),
            itemCount,
            geometry.ListBounds.Height,
            ref cursor,
            ref scrollTop,
            ref scrollbarDrag);
    }

    private static bool TryHandleListMouse(
        MouseConsoleInputEvent mouse,
        ConsoleSize size,
        int itemCount,
        int scrollTop,
        ref int cursor,
        out bool connect)
    {
        connect = false;
        if (itemCount == 0 ||
            mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click or MouseEventKind.DoubleClick))
        {
            return false;
        }

        var geometry = GetDialogGeometry(size, itemCount);
        if (mouse.X < geometry.ListBounds.X ||
            mouse.X >= geometry.ListBounds.Right ||
            mouse.Y < geometry.ListBounds.Y ||
            mouse.Y >= geometry.ListBounds.Bottom)
        {
            return false;
        }

        int index = scrollTop + mouse.Y - geometry.ListBounds.Y;
        if (index < 0 || index >= itemCount)
            return false;

        cursor = index;
        connect = mouse.Kind == MouseEventKind.DoubleClick;
        return true;
    }

    private static int NormalizeListScroll(int itemCount, int visibleRows, int cursor, int scrollTop)
    {
        scrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, itemCount, visibleRows);
        if (itemCount > 0)
            scrollTop = ScrollStateCalculator.EnsureIndexVisible(cursor, scrollTop, visibleRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, itemCount, visibleRows);
    }

    private static DialogGeometry GetDialogGeometry(ConsoleSize size, int itemCount)
    {
        int width = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int targetListRows = Math.Min(MaxVisibleRows, Math.Max(1, itemCount));
        int height = Math.Min(targetListRows + 7, Math.Max(8, size.Height - 2));
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
        var listBounds = new Rect(
            contentBounds.X + 2,
            contentBounds.Y,
            Math.Max(1, contentBounds.Width - 4),
            Math.Max(1, buttonY - contentBounds.Y - 1));
        return new DialogGeometry(bounds, frameBounds, listBounds, buttonY);
    }

    private static string SecurityLabel(FtpConnectionSecurityMode mode) =>
        mode switch
        {
            FtpConnectionSecurityMode.PlainFtp => "FTP plain",
            FtpConnectionSecurityMode.ExplicitFtps => "FTPS explicit",
            FtpConnectionSecurityMode.ImplicitFtps => "FTPS implicit",
            FtpConnectionSecurityMode.Auto => "FTP/FTPS auto",
            _ => mode.ToString(),
        };

    private static string Truncate(string text, int maxLen)
    {
        if (maxLen <= 0)
            return string.Empty;
        return text.Length <= maxLen ? text : text[..maxLen];
    }

    private readonly record struct DialogGeometry(
        Rect Bounds,
        Rect FrameBounds,
        Rect ListBounds,
        int ButtonY);
}
