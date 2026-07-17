using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationCommandLineKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationCommandLineKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ConsoleKeyInfo key, ApplicationUiFrame frame)
    {
        if (TryHandleFarCommandLineShortcut(key))
            return ApplicationInputHandlingResult.FromHandled(true);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine || _context.CommandLine.HasText)
                _context.CommandLine.SelectAll();
            else
                _context.SelectAllCommandLineTextOrPanelItems();
            return ApplicationInputHandlingResult.FromHandled(true);
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return ApplicationInputHandlingResult.FromHandled(_context.CopyCommandLineSelection());

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return ApplicationInputHandlingResult.FromHandled(_context.PasteTextIntoCommandLine());

        if (TryHandleNavigationKey(key, forceCommandLine: frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine))
            return ApplicationInputHandlingResult.FromHandled(true);

        switch (key.Key)
        {
            case ConsoleKey.Delete:
                ResetNavigation(frame);
                _context.CommandLine.DeleteForward();
                NotifyVisibleEdit(frame);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Backspace:
                ResetNavigation(frame);
                _context.CommandLine.DeleteBack();
                NotifyVisibleEdit(frame);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Escape:
                ResetNavigation(frame);
                _context.CommandLine.Clear();
                _context.HideCommandCompletion(false);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.Enter:
                ResetNavigation(frame);
                if (_context.CommandLine.HasText)
                    _context.ExecuteCommand(_context.CommandLine.Text);
                return ApplicationInputHandlingResult.FromHandled(true);

            case ConsoleKey.UpArrow when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                return ApplicationInputHandlingResult.FromHandled(
                    _context.BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest));

            case ConsoleKey.DownArrow when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                return ApplicationInputHandlingResult.FromHandled(
                    _context.BrowseCommandHistory(+1, CommandHistoryNavigationStart.Oldest));

            case ConsoleKey.F10 when frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine:
                _context.SetRunning(false);
                return ApplicationInputHandlingResult.FromHandled(false);
        }

        if (ApplicationKeyboardTargetResolver.IsPrintable(key))
        {
            ResetNavigation(frame);
            _context.CommandLine.Insert(key.KeyChar);
            NotifyVisibleEdit(frame);
            return ApplicationInputHandlingResult.FromHandled(true);
        }

        return ApplicationInputHandlingResult.NotHandled;
    }

    private bool TryHandleNavigationKey(ConsoleKeyInfo key, bool forceCommandLine)
    {
        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        if (hasAlt)
            return false;

        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool shouldUseCommandLine = forceCommandLine ||
            _context.CommandLine.HasText ||
            _context.CommandLine.HasSelection ||
            hasControl ||
            hasShift;

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow when shouldUseCommandLine:
                if (hasControl && hasShift)
                    _context.CommandLine.MoveToPreviousWordWithSelection();
                else if (hasControl)
                    _context.CommandLine.MoveToPreviousWord();
                else if (hasShift)
                    _context.CommandLine.MoveCursorWithSelection(_context.CommandLine.CursorPosition - 1);
                else
                    _context.CommandLine.MoveCursor(-1);
                _context.ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.RightArrow when shouldUseCommandLine:
                if (hasControl && hasShift)
                    _context.CommandLine.MoveToNextWordWithSelection();
                else if (hasControl)
                    _context.CommandLine.MoveToNextWord();
                else if (hasShift)
                    _context.CommandLine.MoveCursorWithSelection(_context.CommandLine.CursorPosition + 1);
                else
                    _context.CommandLine.MoveCursor(+1);
                _context.ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.Home when shouldUseCommandLine:
                if (hasShift)
                    _context.CommandLine.MoveCursorWithSelection(0);
                else
                    _context.CommandLine.MoveToStart();
                _context.ResetCommandHistoryNavigation();
                return true;

            case ConsoleKey.End when shouldUseCommandLine:
                if (hasShift)
                    _context.CommandLine.MoveCursorWithSelection(_context.CommandLine.Text.Length);
                else
                    _context.CommandLine.MoveToEnd();
                _context.ResetCommandHistoryNavigation();
                return true;

            default:
                return false;
        }
    }

    private bool TryHandleFarCommandLineShortcut(ConsoleKeyInfo key)
    {
        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.E, '\u0005'))
            return _context.BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.X, '\u0018'))
            return _context.BrowseCommandHistory(+1, CommandHistoryNavigationStart.Newest);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.F, '\u0006'))
            return InsertCurrentItemFullPathIntoCommandLine();

        if (KeyboardShortcutClassifier.IsPlainControlEnter(key))
            return InsertCurrentItemNameIntoCommandLine();

        if (KeyboardShortcutClassifier.IsPlainControlOpenBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_context.LeftPanel());

        if (KeyboardShortcutClassifier.IsPlainControlCloseBracket(key))
            return InsertPanelCurrentDirectoryIntoCommandLine(_context.RightPanel());

        return false;
    }

    private bool InsertCurrentItemNameIntoCommandLine()
    {
        var item = _context.PanelController.CurrentItem(_context.ActiveState());
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.Name);
        return true;
    }

    private bool InsertCurrentItemFullPathIntoCommandLine()
    {
        var item = _context.PanelController.CurrentItem(_context.ActiveState());
        if (item is null)
            return true;

        InsertTextIntoCommandLine(item.FullPath);
        return true;
    }

    private bool InsertPanelCurrentDirectoryIntoCommandLine(FilePanelState state)
    {
        string path = state.CurrentDirectory;
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            path += Path.DirectorySeparatorChar;
        InsertTextIntoCommandLine(path);
        return true;
    }

    private void InsertTextIntoCommandLine(string text)
    {
        _context.CommandLine.InsertText(QuoteCommandLineInsertion(text));

        if (_context.IsPanelsMode())
            _context.OnVisibleCommandLineTextEdited();
        else
            _context.ResetCommandHistoryNavigation();
    }

    private void NotifyVisibleEdit(ApplicationUiFrame frame)
    {
        if (frame.Mode == ApplicationWorkspaceMode.Panels)
            _context.OnVisibleCommandLineTextEdited();
    }

    private void ResetNavigation(ApplicationUiFrame frame)
    {
        if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine)
            _context.ResetCommandHistoryNavigation();
    }

    private static string QuoteCommandLineInsertion(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;
}
