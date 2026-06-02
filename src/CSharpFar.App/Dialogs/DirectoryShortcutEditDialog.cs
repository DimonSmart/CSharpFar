using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

internal sealed record DirectoryShortcutEditResult(
    bool Accepted,
    AppSettings.DirectoryShortcutItem? Item);

internal sealed class DirectoryShortcutEditDialog
{
    private const int DialogWidth = 62;
    private const int DialogHeight = 10;
    private const int LabelWidth = 8;

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar = new(
    [
        new DialogButton("ok", "OK", 'O', IsDefault: true),
        new DialogButton("cancel", "Cancel", 'C'),
    ]);

    public DirectoryShortcutEditDialog(ScreenRenderer screen, ConsolePalette? palette = null)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
    }

    public DirectoryShortcutEditResult Show(
        int number,
        AppSettings.DirectoryShortcutItem? currentItem,
        string activePanelPath)
    {
        var name = Buffer(currentItem?.Name ?? DirectoryShortcutNormalizer.GetDefaultNameFromPath(activePanelPath));
        var path = Buffer(currentItem?.Path ?? activePanelPath);
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        int focusRow = 0;
        int focusedButton = 0;
        string? error = null;

        try
        {
            while (true)
            {
                var layout = Draw(number, name, path, focusRow, focusedButton);
                var input = _screen.ReadInput();
                if (input is MouseConsoleInputEvent mouse &&
                    TryFocusField(mouse, layout.ContentBounds, ref focusRow))
                {
                    continue;
                }

                if (_buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId) &&
                    (focusRow == 2 || input is MouseConsoleInputEvent))
                {
                    focusRow = 2;
                    if (buttonId == "cancel")
                        return new DirectoryShortcutEditResult(false, currentItem);
                    if (buttonId == "ok")
                        return Accepted(number, name.Text, path.Text);
                    continue;
                }

                if (input is not KeyConsoleInputEvent { Key: var key })
                    continue;

                if (key.Key == ConsoleKey.Escape)
                    return new DirectoryShortcutEditResult(false, currentItem);

                if (key.Key is ConsoleKey.Tab or ConsoleKey.DownArrow)
                {
                    focusRow = Math.Min(2, focusRow + 1);
                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow)
                {
                    focusRow = Math.Max(0, focusRow - 1);
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    if (focusRow < 2)
                        focusRow++;
                    else
                        return Accepted(number, name.Text, path.Text);
                    continue;
                }

                var buffer = focusRow == 0 ? name : focusRow == 1 ? path : null;
                if (buffer is not null)
                    SingleLineTextInput.HandleKey(buffer, key, ref error);
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private ModalDialogRenderer.Layout Draw(
        int number,
        CommandLineState name,
        CommandLineState path,
        int focusRow,
        int focusedButton)
    {
        using var frame = _screen.BeginFrame();
        Rect outerBounds = _modalRenderer.CenteredOuterBounds(_screen, DialogWidth, DialogHeight);
        ModalDialogRenderer.Layout layout = default;
        _modalRenderer.Render(
            _screen,
            outerBounds,
            $"Directory shortcut {number}",
            doubleBorder: true,
            PaletteStyles.DialogPopupOptions(_palette) with { DrawBorder = false },
            PaletteStyles.DialogPopupOptions(_palette) with { DrawShadow = false },
            (_, currentLayout) =>
            {
                layout = currentLayout;
                Rect content = currentLayout.ContentBounds;
                DrawField(content, content.Y, "Name", name, focusRow == 0);
                DrawField(content, content.Y + 2, "Path", path, focusRow == 1);
                _buttonBar.Render(
                    _screen,
                    content.X,
                    content.Y + 5,
                    content.Width,
                    focusRow == 2 ? focusedButton : -1,
                    PaletteStyles.DialogFill(_palette),
                    PaletteStyles.InputField(_palette));

                if (focusRow is 0 or 1)
                {
                    var buffer = focusRow == 0 ? name : path;
                    int fieldWidth = Math.Max(1, content.Width - LabelWidth);
                    int cursorX = content.X + LabelWidth + Math.Min(buffer.CursorPosition, fieldWidth - 1);
                    _screen.SetCursorPosition(cursorX, content.Y + focusRow * 2);
                    _screen.SetCursorVisible(true);
                }
                else
                {
                    _screen.SetCursorVisible(false);
                }
            });
        return layout;
    }

    private void DrawField(Rect content, int y, string label, CommandLineState buffer, bool focused)
    {
        _screen.Write(content.X, y, label.PadRight(LabelWidth), PaletteStyles.DialogFill(_palette));
        SingleLineTextInput.Render(
            _screen,
            content.X + LabelWidth,
            y,
            Math.Max(1, content.Width - LabelWidth),
            buffer,
            focused ? PaletteStyles.InputField(_palette) : PaletteStyles.DialogFill(_palette),
            PaletteStyles.InputHighlight(_palette));
    }

    private static DirectoryShortcutEditResult Accepted(int number, string name, string path)
    {
        path = path.Trim();
        return new DirectoryShortcutEditResult(
            true,
            path.Length == 0
                ? null
                : new AppSettings.DirectoryShortcutItem
                {
                    Number = number,
                    Name = DirectoryShortcutNormalizer.NormalizeName(name),
                    Path = path,
                });
    }

    private static bool TryFocusField(MouseConsoleInputEvent mouse, Rect content, ref int focusRow)
    {
        if (mouse.Button != MouseButton.Left ||
            mouse.Kind is not (MouseEventKind.Down or MouseEventKind.Click))
        {
            return false;
        }

        if (mouse.Y == content.Y && mouse.X >= content.X + LabelWidth && mouse.X < content.Right)
        {
            focusRow = 0;
            return true;
        }

        if (mouse.Y == content.Y + 2 && mouse.X >= content.X + LabelWidth && mouse.X < content.Right)
        {
            focusRow = 1;
            return true;
        }

        return false;
    }

    private static CommandLineState Buffer(string text)
    {
        var buffer = new CommandLineState();
        buffer.SetText(text);
        return buffer;
    }
}
