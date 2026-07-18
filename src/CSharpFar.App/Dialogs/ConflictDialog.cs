using System.Globalization;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Dialogs;

/// <summary>Shows a "file already exists" dialog and returns the user's choice.</summary>
internal sealed class ConflictDialog
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 13;
    private const string OverwriteButton = "overwrite";
    private const string SkipButton = "skip";
    private const string RenameButton = "rename";
    private const string CancelButton = "cancel";

    private readonly ModalDialogHost _modalDialogs;
    private readonly ConsolePalette _palette;
    private readonly ModalDialogRenderer _modalRenderer = new();
    private readonly DialogButtonBar _buttonBar;

    public ConflictDialog(ModalDialogHost modalDialogs, ConsolePalette? palette = null)
    {
        _modalDialogs = modalDialogs;
        _palette = palette ?? PaletteRegistry.Default;
        _buttonBar = CreateButtons();
    }

    public FileOperationConflictDecision Show(FileOperationConflict conflict)
    {
        bool rememberChoice = false;
        var rememberChoiceLine = new CheckBoxLine("Remember choice");
        int focusSection = 1;
        int focusedButton = 0;

        return _modalDialogs.Run(
            context => Draw(conflict, context, rememberChoiceLine, focusSection, focusedButton),
            (input, frame) =>
            {
                if ((focusSection == 1 || input is MouseConsoleInputEvent) &&
                    _buttonBar.TryHandleInput(input, frame.Buttons, ref focusedButton, out string? buttonId))
                {
                    focusSection = 1;
                    if (buttonId is not null)
                        return ModalDialogLoopResult<FileOperationConflictDecision>.Complete(BuildDecision(buttonId, rememberChoice, conflict));
                    return ModalDialogLoopResult<FileOperationConflictDecision>.Continue;
                }

                switch (input)
                {
                    case KeyConsoleInputEvent { Key: var key }:
                        if (key.Key == ConsoleKey.Escape)
                            return ModalDialogLoopResult<FileOperationConflictDecision>.Complete(
                                FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel));

                        if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Tab)
                        {
                            focusSection = focusSection == 0 ? 1 : 0;
                            return ModalDialogLoopResult<FileOperationConflictDecision>.Continue;
                        }

                        if (focusSection == 0 && key.Key is (ConsoleKey.Spacebar or ConsoleKey.Enter))
                        {
                            rememberChoiceLine.TryHandleKey(key);
                            rememberChoice = rememberChoiceLine.Value;
                            return ModalDialogLoopResult<FileOperationConflictDecision>.Continue;
                        }

                        break;
                    case MouseConsoleInputEvent mouse:
                        if (rememberChoiceLine.TryHandleMouse(mouse, frame.RememberChoiceBounds))
                        {
                            focusSection = 0;
                            rememberChoice = rememberChoiceLine.Value;
                        }

                        break;
                }

                return ModalDialogLoopResult<FileOperationConflictDecision>.Continue;
            },
            prepareRender: () => rememberChoiceLine.Value = rememberChoice);
    }

    private ConflictDialogFrame Draw(
        FileOperationConflict conflict,
        UiRenderContext context,
        CheckBoxLine rememberChoiceLine,
        int focusSection,
        int focusedButton)
    {
        Rect rememberChoiceBounds = default;
        DialogButtonBarLayout buttons = null!;
        int dlgX = Math.Max(0, (context.Size.Width - DialogWidth) / 2);
        int dlgY = Math.Max(0, (context.Size.Height - DialogHeight) / 2);
        var bounds = new Rect(dlgX, dlgY, DialogWidth, DialogHeight);

        _modalRenderer.Render(context.Screen, bounds, "Warning", true, WarningDialogStyles.OuterOptions, WarningDialogStyles.FrameOptions, (_, layout) =>
        {
            Rect frameBounds = layout.FrameBounds;
            int contentX = frameBounds.X + 2;
            int contentWidth = Math.Max(1, frameBounds.Width - 4);

            context.Screen.Write(contentX, frameBounds.Y + 1, Center("File already exists", contentWidth), WarningDialogStyles.Fill);
            context.Screen.Write(contentX, frameBounds.Y + 2, ShortenMiddle(conflict.DestinationPath, contentWidth).PadRight(contentWidth), WarningDialogStyles.Fill);

            context.Screen.Write(contentX, frameBounds.Y + 4, BuildInfoLine("New", conflict.SourceSize, conflict.SourceLastWriteTime, contentWidth), WarningDialogStyles.Fill);
            context.Screen.Write(contentX, frameBounds.Y + 5, BuildInfoLine("Existing", conflict.DestinationSize, conflict.DestinationLastWriteTime, contentWidth), WarningDialogStyles.Fill);

            rememberChoiceBounds = new Rect(contentX, frameBounds.Y + 7, contentWidth, 1);
            rememberChoiceLine.Render(
                context.Screen,
                rememberChoiceBounds.X,
                rememberChoiceBounds.Y,
                rememberChoiceBounds.Width,
                focusSection == 0,
                WarningDialogStyles.Fill,
                WarningDialogStyles.ButtonFocus);

            DrawSeparator(context.Screen, frameBounds, frameBounds.Y + 8);
            buttons = _buttonBar.Render(
                context.Screen,
                contentX,
                frameBounds.Y + 9,
                contentWidth,
                focusedButton,
                WarningDialogStyles.Fill,
                focusSection == 1 ? WarningDialogStyles.ButtonFocus : WarningDialogStyles.Fill);
        });
        context.Screen.SetCursorVisible(false);
        return new ConflictDialogFrame(rememberChoiceBounds, buttons);
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
            _ => FileOperationConflictDecision.FromMode(ConflictDecisionMode.Cancel),
        };
    }

    private FileOperationConflictDecision BuildRenameDecision(bool rememberChoice, FileOperationConflict conflict)
    {
        if (rememberChoice)
            return FileOperationConflictDecision.FromMode(ConflictDecisionMode.RenameAll);

        string? renamed = new InputDialog(_modalDialogs).Show(
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

    private static DialogButtonBar CreateButtons()
    {
        var buttons = new List<DialogButton>
        {
            new(OverwriteButton, "Overwrite", 'O', IsDefault: true),
            new(SkipButton, "Skip", 'S'),
            new(RenameButton, "Rename", 'R'),
        };

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

    private static void DrawSeparator(ScreenRenderer screen, Rect frameBounds, int y)
    {
        screen.WriteChar(frameBounds.X, y, '╟', WarningDialogStyles.Border);
        screen.Write(frameBounds.X + 1, y, new string('─', Math.Max(0, frameBounds.Width - 2)), WarningDialogStyles.Border);
        screen.WriteChar(frameBounds.Right - 1, y, '╢', WarningDialogStyles.Border);
    }

    private readonly record struct ConflictDialogFrame(
        Rect RememberChoiceBounds,
        DialogButtonBarLayout Buttons);
}
