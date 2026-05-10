using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using System.Globalization;

namespace CSharpFar.App.Dialogs;

/// <summary>Shows a "file already exists" dialog and returns the user's choice.</summary>
internal sealed class ConflictDialog
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 13;
    private const string OverwriteButton = "overwrite";
    private const string SkipButton = "skip";
    private const string RenameButton = "rename";
    private const string AppendButton = "append";
    private const string CancelButton = "cancel";

    private readonly ScreenRenderer _screen;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar;
    private Rect _rememberBounds;

    public ConflictDialog(ScreenRenderer screen, ConsolePalette? palette = null, bool allowAppend = true)
    {
        _screen = screen;
        _palette = palette ?? PaletteRegistry.Default;
        _buttonBar = CreateButtons(allowAppend);
    }

    public FileOperationConflictDecision Show(FileOperationConflict conflict)
    {
        var size = _screen.GetSize();
        var saved = _screen.Capture(new Rect(0, 0, size.Width, size.Height));
        bool rememberChoice = false;
        int focusSection = 1;
        int focusedButton = 0;

        try
        {
            _screen.SetCursorVisible(false);

            while (true)
            {
                Draw(conflict, size, rememberChoice, focusSection, focusedButton);
                var input = _screen.ReadInput();

                if ((focusSection == 1 || input is MouseConsoleInputEvent) &&
                    _buttonBar.TryHandleInput(input, ref focusedButton, out string? buttonId))
                {
                    focusSection = 1;
                    if (buttonId is not null)
                        return BuildDecision(buttonId, rememberChoice, conflict);
                    continue;
                }

                switch (input)
                {
                    case KeyConsoleInputEvent { Key: var key }:
                        if (key.Key == ConsoleKey.Escape)
                            return FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel);

                        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Tab)
                        {
                            focusSection = focusSection == 0 ? 1 : 0;
                            continue;
                        }

                        if (focusSection == 0 && key.Key is (ConsoleKey.Spacebar or ConsoleKey.Enter))
                        {
                            rememberChoice = !rememberChoice;
                            continue;
                        }

                        break;
                    case MouseConsoleInputEvent mouse:
                        if (IsRememberClick(mouse))
                        {
                            focusSection = 0;
                            rememberChoice = !rememberChoice;
                        }

                        break;
                }
            }
        }
        finally
        {
            _screen.Restore(saved);
            _screen.SetCursorVisible(false);
        }
    }

    private void Draw(FileOperationConflict conflict, ConsoleSize size, bool rememberChoice, int focusSection, int focusedButton)
    {
        int dlgX = Math.Max(0, (size.Width - DialogWidth) / 2);
        int dlgY = Math.Max(0, (size.Height - DialogHeight) / 2);
        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);

        using var frame = _screen.BeginFrame();

        _modalRenderer.Render(_screen, bounds, "Warning", true, WarningDialogStyles.OuterOptions, WarningDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect frameBounds = layout.FrameBounds;
            int contentX = frameBounds.X + 2;
            int contentWidth = Math.Max(1, frameBounds.Width - 4);

            _screen.Write(contentX, frameBounds.Y + 1, Center("File already exists", contentWidth), WarningDialogStyles.Fill);
            _screen.Write(contentX, frameBounds.Y + 2, ShortenMiddle(conflict.DestinationPath, contentWidth).PadRight(contentWidth), WarningDialogStyles.Fill);

            _screen.Write(contentX, frameBounds.Y + 4, BuildInfoLine("New", conflict.SourceSize, conflict.SourceLastWriteTime, contentWidth), WarningDialogStyles.Fill);
            _screen.Write(contentX, frameBounds.Y + 5, BuildInfoLine("Existing", conflict.DestinationSize, conflict.DestinationLastWriteTime, contentWidth), WarningDialogStyles.Fill);

            var rememberStyle = focusSection == 0 ? WarningDialogStyles.ButtonFocus : WarningDialogStyles.Fill;
            string remember = $"[{(rememberChoice ? "x" : " ")}] Remember choice";
            _screen.Write(contentX, frameBounds.Y + 7, remember.PadRight(contentWidth), rememberStyle);
            _rememberBounds = new Rect(contentX, frameBounds.Y + 7, remember.Length, 1);

            DrawSeparator(frameBounds, frameBounds.Y + 8);
            _buttonBar.Render(
                _screen,
                contentX,
                frameBounds.Y + 9,
                contentWidth,
                focusedButton,
                WarningDialogStyles.Fill,
                focusSection == 1 ? WarningDialogStyles.ButtonFocus : WarningDialogStyles.Fill);
        });
    }

    private FileOperationConflictDecision BuildDecision(string buttonId, bool rememberChoice, FileOperationConflict conflict)
    {
        return buttonId switch
        {
            OverwriteButton => FileOperationConflictDecision.FromMode(
                rememberChoice ? ConflictDecisionMode.OverwriteAll : ConflictDecisionMode.Overwrite),
            SkipButton => FileOperationConflictDecision.FromMode(
                rememberChoice ? ConflictDecisionMode.SkipAll : ConflictDecisionMode.Skip),
            RenameButton => BuildRenameDecision(rememberChoice, conflict),
            AppendButton => FileOperationConflictDecision.FromMode(
                rememberChoice ? ConflictDecisionMode.AppendAll : ConflictDecisionMode.Append),
            _ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
        };
    }

    private FileOperationConflictDecision BuildRenameDecision(bool rememberChoice, FileOperationConflict conflict)
    {
        if (rememberChoice)
            return FileOperationConflictDecision.FromMode(ConflictDecisionMode.RenameAll);

        string? renamed = new InputDialog(_screen, _palette).Show(
            "Rename",
            "New destination:",
            initialText: conflict.DestinationPath);
        return string.IsNullOrWhiteSpace(renamed)
            ? FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip)
            : new FileOperationConflictDecision
            {
                Mode = ConflictDecisionMode.Rename,
                NewDestinationPath = renamed,
            };
    }

    private static DialogButtonBar CreateButtons(bool allowAppend)
    {
        var buttons = new List<DialogButton>
        {
            new(OverwriteButton, "Overwrite", 'O', IsDefault: true),
            new(SkipButton, "Skip", 'S'),
            new(RenameButton, "Rename", 'R'),
        };

        if (allowAppend)
            buttons.Add(new DialogButton(AppendButton, "Append", 'A'));

        buttons.Add(new DialogButton(CancelButton, "Cancel", 'C'));
        return new DialogButtonBar(buttons);
    }

    private static string BuildInfoLine(string label, long? size, DateTime? lastWriteTime, int width)
    {
        string right = $"{FormatSize(size)} {FormatDate(lastWriteTime)}".TrimEnd();
        int rightWidth = Math.Max(0, width - label.Length);
        return label + ShortenLeft(right, rightWidth).PadLeft(rightWidth);
    }

    private static string FormatSize(long? size) =>
        size is null
            ? "n/a"
            : size.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string FormatDate(DateTime? time) =>
        time is null ? string.Empty : time.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Center(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        int left = (width - text.Length) / 2;
        return new string(' ', left) + text + new string(' ', width - left - text.Length);
    }

    private static string ShortenMiddle(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        if (value.Length <= maxLength)
            return value;
        if (maxLength <= 1)
            return "…";

        int left = (maxLength - 1) / 2;
        int right = maxLength - left - 1;
        return value[..left] + "…" + value[^right..];
    }

    private static string ShortenLeft(string value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;
        return value.Length <= maxLength ? value : "…" + value[^Math.Max(0, maxLength - 1)..];
    }

    private void DrawSeparator(Rect frameBounds, int y)
    {
        _screen.WriteChar(frameBounds.X, y, '╟', WarningDialogStyles.Border);
        _screen.Write(frameBounds.X + 1, y, new string('─', Math.Max(0, frameBounds.Width - 2)), WarningDialogStyles.Border);
        _screen.WriteChar(frameBounds.Right - 1, y, '╢', WarningDialogStyles.Border);
    }

    private bool IsRememberClick(MouseConsoleInputEvent mouse) =>
        mouse.Button == MouseButton.Left &&
        mouse.Kind is MouseEventKind.Down or MouseEventKind.Click &&
        mouse.X >= _rememberBounds.X &&
        mouse.X < _rememberBounds.Right &&
        mouse.Y >= _rememberBounds.Y &&
        mouse.Y < _rememberBounds.Bottom;
}
