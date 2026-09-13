using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationCommandLineKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationCommandLineKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationKeyboardInput input)
    {
        ConsoleKeyInfo key = input.Key;
        ApplicationUiFrame frame = input.Frame;

        if (TryHandleFarCommandLineShortcut(input))
            return ApplicationInputHandlingResult.FromHandled(true);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine || frame.Keyboard.CommandLineHasText)
                return HandleStandardEditing(key, frame, resetHistoryNavigation: false);

            _context.ToggleSelectAllPanelItems(frame.Keyboard.ActiveSide);
            return ApplicationInputHandlingResult.FromHandled(true);
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return ApplicationInputHandlingResult.FromHandled(_context.CopyCommandLineSelection());

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return CommandLineChanged(_context.PasteTextIntoCommandLine(frame.Mode));

        if (ShouldUseStandardNavigation(key, frame) ||
            key.Key is ConsoleKey.Delete or ConsoleKey.Backspace)
        {
            return HandleStandardEditing(key, frame);
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                ResetNavigation(frame);
                _context.CommandLine.Clear();
                _context.HideCommandCompletion(false);
                return CommandLineChanged();

            case ConsoleKey.Enter when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                ResetNavigation(frame);
                _context.CommandLine.Insert('\n');
                NotifyCommandLineEdit();
                return CommandLineChanged();

            case ConsoleKey.Enter:
                ResetNavigation(frame);
                if (frame.Keyboard.CommandLineHasText)
                    _context.ExecuteCommand(_context.CommandLine.Text);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.UpArrow when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                return CommandLineChanged(
                    _context.BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest));

            case ConsoleKey.DownArrow when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                return CommandLineChanged(
                    _context.BrowseCommandHistory(+1, CommandHistoryNavigationStart.Oldest));

            case ConsoleKey.F10 when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                _context.SetRunning(false);
                return ApplicationInputHandlingResult.FromHandled(
                    shouldRender: false,
                    resumesHiddenInteraction: false);
        }

        if (ApplicationKeyboardTargetResolver.IsPrintable(key))
            return HandleStandardEditing(key, frame);

        return ApplicationInputHandlingResult.NotHandled;
    }

    private ApplicationInputHandlingResult HandleStandardEditing(
        ConsoleKeyInfo key,
        ApplicationUiFrame frame,
        bool resetHistoryNavigation = true)
    {
        string? error = null;
        TextInputKeyResult result = SingleLineTextInput.HandleKey(
            _context.CommandLine,
            key,
            ref error);
        if (result == TextInputKeyResult.Ignored)
            return ApplicationInputHandlingResult.NotHandled;

        if (resetHistoryNavigation)
            ResetNavigation(frame);
        if (result == TextInputKeyResult.TextChanged)
            NotifyCommandLineEdit();
        return CommandLineChanged();
    }

    private static bool ShouldUseStandardNavigation(ConsoleKeyInfo key, ApplicationUiFrame frame)
    {
        if (key.Key is not (ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Home or ConsoleKey.End) ||
            (key.Modifiers & ConsoleModifiers.Alt) != 0)
        {
            return false;
        }

        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        return frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine ||
            frame.Keyboard.CommandLineHasText ||
            frame.Keyboard.CommandLineHasSelection ||
            hasControl ||
            hasShift;
    }

    private bool TryHandleFarCommandLineShortcut(ApplicationKeyboardInput input)
    {
        ConsoleKeyInfo key = input.Key;
        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.E, '\u0005'))
            return _context.BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.X, '\u0018'))
            return _context.BrowseCommandHistory(+1, CommandHistoryNavigationStart.Newest);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.F, '\u0006'))
            return InsertCurrentItemFullPathIntoCommandLine(input);

        if (KeyboardShortcutClassifier.IsPlainControlEnter(key))
            return InsertCurrentItemNameIntoCommandLine(input);

        if (KeyboardShortcutClassifier.IsPlainControlOpenBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(input.Panel(PanelSide.Left), input.Frame.Mode);

        if (KeyboardShortcutClassifier.IsPlainControlCloseBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(input.Panel(PanelSide.Right), input.Frame.Mode);

        return false;
    }

    private bool InsertCurrentItemNameIntoCommandLine(ApplicationKeyboardInput input)
    {
        if (!ApplicationCommandContext.TryResolveCommittedCurrentItem(StateForSide(input.ActiveSide), input.ActivePanel, _context.PanelController, out _))
            return true;

        InsertTextIntoCommandLine(input.ActivePanel.CurrentItemName!, input.Frame.Mode);
        return true;
    }

    private bool InsertCurrentItemFullPathIntoCommandLine(ApplicationKeyboardInput input)
    {
        if (!ApplicationCommandContext.TryResolveCommittedCurrentItem(StateForSide(input.ActiveSide), input.ActivePanel, _context.PanelController, out _))
            return true;

        InsertTextIntoCommandLine(input.ActivePanel.CurrentItemFullPath!, input.Frame.Mode);
        return true;
    }

    private bool InsertPanelCurrentDirectoryIntoCommandLine(ApplicationPanelKeyboardFrame frame, ApplicationWorkspaceMode mode)
    {
        string path = frame.CurrentDirectory;
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            path += Path.DirectorySeparatorChar;
        InsertTextIntoCommandLine(path, mode);
        return true;
    }

    private void InsertTextIntoCommandLine(string text, ApplicationWorkspaceMode mode)
    {
        _context.CommandLine.InsertText(QuoteCommandLineInsertion(text));
        _context.OnCommandLineTextEdited();
    }

    private void NotifyCommandLineEdit() =>
        _context.OnCommandLineTextEdited();

    private void ResetNavigation(ApplicationUiFrame frame)
    {
        if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine)
            _context.ResetCommandHistoryNavigation();
    }

    private static string QuoteCommandLineInsertion(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;

    private FilePanelState StateForSide(PanelSide side) =>
        side == PanelSide.Left ? _context.LeftPanel() : _context.RightPanel();

    private static ApplicationInputHandlingResult CommandLineChanged(bool changed = true) =>
        ApplicationInputHandlingResult.FromHandled(
            changed,
            ApplicationRenderPart.CommandLine | ApplicationRenderPart.Completion);
}
