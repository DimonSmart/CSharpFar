using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Dialogs;

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
    private readonly ConsolePalette _palette;

    public FtpConnectionManagerDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
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
        ScrollBarDragState? scrollbarDrag = null;

        while (true)
        {
            int visibleRows = Math.Min(Math.Max(1, connections.Count), MaxVisibleRows);
            Draw(connections, size, cursor, scrollTop, visibleRows);

            var input = _screen.ReadInput();
            if (input is MouseConsoleInputEvent mouse &&
                TryHandleScrollbarMouse(mouse, size, connections.Count, visibleRows, ref cursor, ref scrollTop, ref scrollbarDrag))
            {
                continue;
            }

            if (input is not KeyConsoleInputEvent { Key: var key })
                continue;

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                case ConsoleKey.F10:
                    return null;
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
                    if (cursor > 0)
                    {
                        cursor--;
                        if (cursor < scrollTop)
                            scrollTop = cursor;
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (cursor < connections.Count - 1)
                    {
                        cursor++;
                        if (cursor >= scrollTop + visibleRows)
                            scrollTop = cursor - visibleRows + 1;
                    }
                    break;
                case ConsoleKey.PageUp:
                    cursor = Math.Max(0, cursor - visibleRows);
                    scrollTop = Math.Max(0, scrollTop - visibleRows);
                    break;
                case ConsoleKey.PageDown:
                    cursor = Math.Min(Math.Max(0, connections.Count - 1), cursor + visibleRows);
                    scrollTop = Math.Max(0, cursor - visibleRows + 1);
                    break;
                case ConsoleKey.Home:
                    cursor = 0;
                    scrollTop = 0;
                    break;
                case ConsoleKey.End:
                    cursor = Math.Max(0, connections.Count - 1);
                    scrollTop = Math.Max(0, cursor - visibleRows + 1);
                    break;
            }
        }
    }

    private void Draw(
        IReadOnlyList<FtpConnectionInfo> connections,
        ConsoleSize size,
        int cursor,
        int scrollTop,
        int visibleRows)
    {
        using var frame = _screen.BeginFrame();

        int width = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int listRows = Math.Min(MaxVisibleRows, Math.Max(1, visibleRows));
        int height = Math.Min(size.Height - 2, listRows + 6);
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var bounds = new Rect(x, y, width, height);
        var scrollState = connections.Count > listRows
            ? new ScrollState
            {
                TotalItems = connections.Count,
                ViewportItems = listRows,
                FirstVisibleIndex = scrollTop,
            }
            : null;

        new DialogFrameRenderer().RenderFrame(_screen, bounds, "FTP/FTPS connections", true, PaletteStyles.DialogPopupOptions(_palette), scrollState, (_, contentBounds) =>
        {
            int contentX = contentBounds.X + 2;
            int rowWidth = Math.Max(1, contentBounds.Width - 4);

            if (connections.Count == 0)
            {
                _screen.Write(contentX, contentBounds.Y, "No saved FTP/FTPS connections.".PadRight(rowWidth), PaletteStyles.DialogFill(_palette));
            }
            else
            {
                for (int row = 0; row < listRows; row++)
                {
                    int index = scrollTop + row;
                    if (index >= connections.Count)
                        break;

                    var connection = connections[index];
                    string marker = connection.ShowInDriveSelection ? "*" : " ";
                    string text = $"{marker} {SecurityLabel(connection.SecurityMode)} {connection.DisplayName}  {connection.Username}@{connection.Host}:{connection.Port}";
                    var style = index == cursor ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette);
                    _screen.Write(contentX, contentBounds.Y + row, Truncate(text, rowWidth).PadRight(rowWidth), style);
                }
            }

            int hintY = contentBounds.Y + listRows + 1;
            _screen.Write(contentX, hintY, "Enter Connect  Ins/New  F4/Edit  Del/Delete".PadRight(rowWidth), PaletteStyles.DialogFill(_palette));
            _screen.Write(contentX, hintY + 1, "Esc Close   * shown in drive menu".PadRight(rowWidth), PaletteStyles.DialogFill(_palette));
        });

        _screen.SetCursorVisible(false);
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
        int visibleRows,
        ref int cursor,
        ref int scrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        if (itemCount <= visibleRows)
            return false;

        int width = Math.Min(DialogWidth, Math.Max(44, size.Width - 2));
        int height = Math.Min(size.Height - 2, visibleRows + 6);
        int x = Math.Max(0, (size.Width - width) / 2);
        int y = Math.Max(0, (size.Height - height) / 2);
        var scrollbarBounds = new Rect(x + width - 1, y + 1, 1, visibleRows);

        return ScrollableListMouseHandler.TryHandleScrollbarMouse(
            mouse,
            scrollbarBounds,
            itemCount,
            visibleRows,
            ref cursor,
            ref scrollTop,
            ref scrollbarDrag);
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
}
