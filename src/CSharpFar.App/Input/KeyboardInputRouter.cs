using CSharpFar.App.CommandLine;
using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class KeyboardInputRouter
{
    private readonly KeyboardInputContext _context;

    public KeyboardInputRouter(KeyboardInputContext context)
    {
        _context = context;
    }

    public bool Handle(ConsoleKeyInfo key)
    {
        if (_context.MenuState.OpenState != MenuOpenState.Closed)
        {
            if (!_context.HasVisiblePanels())
            {
                _context.MenuController.Close();
                return true;
            }

            return _context.MenuController.HandleKey(
                key,
                _context.BuildMenuDefinition(),
                _context.ActiveSide());
        }

        var quickSearchResult = _context.PanelQuickSearch.HandleKey(key);
        if (quickSearchResult == PanelQuickSearchKeyResult.Handled)
            return true;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return _context.TogglePanels();

        if (TryHandleFarCommandLineShortcut(key))
            return true;

        if (!_context.HasVisiblePanels())
            return HandleHiddenCommandLineKey(key);

        if (_context.TryHandleFarNetPanelShortcut(key))
            return true;

        if (TryHandleDirectoryShortcut(key))
            return true;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
            return _context.ExecuteRegisteredCommand(MenuCommandIds.SettingsOpenPanelSettings, null);

        if (KeyboardShortcutClassifier.IsPlainControlBackslash(key))
            return _context.ExecuteRegisteredCommand(ApplicationCommandIds.NavigateToRoot, null);

        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                return _context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _context.ActiveSide(),
                        ViewMode = PanelViewMode.Full,
                    });
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                return _context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _context.ActiveSide(),
                        ViewMode = PanelViewMode.BriefTwoColumns,
                    });
            }
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.Q, '\u0011'))
        {
            _context.SetQuickView(!_context.QuickView());
            return true;
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            _context.SelectAllCommandLineTextOrPanelItems();
            return true;
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return _context.CopyCommandLineSelection();

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return _context.PasteTextIntoCommandLine();

        if (TryHandleCommandLineNavigationKey(key, forceCommandLine: false))
            return true;

        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        if (isControlShortcut)
        {
            switch (key.Key)
            {
                case ConsoleKey.Multiply:
                    _context.PanelController.InvertSelection(_context.ActiveState(), _context.PanelOptions());
                    return true;
                case ConsoleKey.D8 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                    _context.PanelController.InvertSelection(_context.ActiveState(), _context.PanelOptions());
                    return true;
            }
        }

        if (TryHandleFunctionKey(key, out bool functionKeyShouldRender))
            return functionKeyShouldRender;

        if (_context.PanelQuickSearch.TryStart(key))
        {
            _context.HideCommandCompletion(false);
            _context.ResetCommandHistoryNavigation();
            return true;
        }

        int vr = _context.VisibleRows();

        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (_context.CommandLine.HasText || _context.CommandLine.HasSelection)
                {
                    _context.CommandLine.MoveCursor(-1);
                    return true;
                }

                _context.MovePanelColumn(-1);
                return true;

            case ConsoleKey.RightArrow:
                if (_context.CommandLine.HasText || _context.CommandLine.HasSelection)
                {
                    _context.CommandLine.MoveCursor(+1);
                    return true;
                }

                _context.MovePanelColumn(+1);
                return true;

            case ConsoleKey.Home:
                if (_context.CommandLine.HasText || _context.CommandLine.HasSelection)
                    _context.CommandLine.MoveToStart();
                else
                    _context.PanelController.MoveToFirst(_context.ActiveState());
                return true;

            case ConsoleKey.End:
                if (_context.CommandLine.HasText || _context.CommandLine.HasSelection)
                    _context.CommandLine.MoveToEnd();
                else
                    _context.PanelController.MoveToLast(_context.ActiveState(), vr);
                return true;

            case ConsoleKey.Delete:
                _context.CommandLine.DeleteForward();
                _context.OnVisibleCommandLineTextEdited();
                return true;

            case ConsoleKey.Backspace:
                bool hadCommandText = _context.CommandLine.HasText;
                if (hadCommandText)
                {
                    _context.CommandLine.DeleteBack();
                    _context.OnVisibleCommandLineTextEdited();
                }
                else
                {
                    _context.HideCommandCompletion(false);
                    _context.TryGoUp();
                }
                return true;

            case ConsoleKey.Escape:
                if (_context.TryHideCommandCompletionTemporarily())
                    return true;

                if (_context.ActiveState().SearchRequest is not null)
                {
                    _context.CloseSearchResultsPanel(_context.ActiveState(), _context.ActiveSide());
                    return true;
                }

                _context.CommandLine.Clear();
                _context.HideCommandCompletion(false);
                return true;

            case ConsoleKey.Enter:
                if (_context.TryAcceptCommandCompletion())
                    return true;

                if (_context.CommandLine.HasText)
                    _context.ExecuteCommand(_context.CommandLine.Text);
                else
                    _context.ExecuteRegisteredCommand(ApplicationCommandIds.OpenCurrentItem, null);
                return true;

            case ConsoleKey.Insert:
                _context.PanelController.ToggleSelection(_context.ActiveState(), vr, _context.PanelOptions());
                return true;

            case ConsoleKey.Tab:
                var otherSide = OtherPanelSide(_context.ActiveSide());
                if (_context.IsPanelVisible(otherSide))
                    _context.SetActiveSide(otherSide);
                else
                    _context.EnsureActivePanelVisible();
                return true;

            case ConsoleKey.UpArrow:
                if (_context.TryMoveCommandCompletionSelection(-1))
                    return true;

                _context.PanelController.MoveCursor(_context.ActiveState(), -1, vr);
                return true;

            case ConsoleKey.DownArrow:
                if (_context.TryMoveCommandCompletionSelection(+1))
                    return true;

                _context.PanelController.MoveCursor(_context.ActiveState(), +1, vr);
                return true;

            case ConsoleKey.PageUp:
                _context.PanelController.MoveCursor(_context.ActiveState(), -vr, vr);
                return true;

            case ConsoleKey.PageDown:
                _context.PanelController.MoveCursor(_context.ActiveState(), +vr, vr);
                return true;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _context.CommandLine.Insert(key.KeyChar);
            _context.OnVisibleCommandLineTextEdited();
            return true;
        }

        return false;
    }

    private bool TryHandleDirectoryShortcut(ConsoleKeyInfo key)
    {
        if ((key.Modifiers & ConsoleModifiers.Control) == 0 ||
            (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) != 0)
        {
            return false;
        }

        int? number = key.Key switch
        {
            ConsoleKey.D0 or ConsoleKey.NumPad0 => 0,
            ConsoleKey.D1 or ConsoleKey.NumPad1 => 1,
            ConsoleKey.D2 or ConsoleKey.NumPad2 => 2,
            ConsoleKey.D3 or ConsoleKey.NumPad3 => 3,
            ConsoleKey.D4 or ConsoleKey.NumPad4 => 4,
            ConsoleKey.D5 or ConsoleKey.NumPad5 => 5,
            ConsoleKey.D6 or ConsoleKey.NumPad6 => 6,
            ConsoleKey.D7 or ConsoleKey.NumPad7 => 7,
            ConsoleKey.D8 or ConsoleKey.NumPad8 => 8,
            ConsoleKey.D9 or ConsoleKey.NumPad9 => 9,
            _ => null,
        };

        return number is not null &&
            _context.ExecuteRegisteredCommand(
                DirectoryShortcutCommandIds.Navigate,
                new NavigateToDirectoryShortcutArgs(number.Value));
    }

    private bool TryHandleCommandLineNavigationKey(ConsoleKeyInfo key, bool forceCommandLine)
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

    private bool TryHandleFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (key.Key is < ConsoleKey.F1 or > ConsoleKey.F12)
            return false;

        if (!FunctionKeyLayerResolver.TryResolveChordLayer(key.Modifiers, out var layer))
            return false;

        var binding = _context.FunctionKeyBindings.FirstOrDefault(candidate =>
            candidate.Layer == layer &&
            candidate.Key == key.Key);

        if (binding is null)
            return false;

        if (!_context.CanExecuteFunctionKeyCommand(binding.CommandId) && !binding.RunsWhenUnavailable)
        {
            shouldRender = true;
            return true;
        }

        shouldRender = _context.ExecuteRegisteredCommand(binding.CommandId, null);
        return true;
    }

    private bool TryHandlePanelVisibilityFunctionKey(ConsoleKeyInfo key, out bool shouldRender)
    {
        shouldRender = false;

        if (!KeyboardShortcutClassifier.HasOnlyControlModifier(key) ||
            key.Key is not (ConsoleKey.F1 or ConsoleKey.F2))
        {
            return false;
        }

        return TryHandleFunctionKey(key, out shouldRender);
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

        if (_context.HasVisiblePanels())
            _context.OnVisibleCommandLineTextEdited();
        else
            _context.ResetCommandHistoryNavigation();
    }

    private bool HandleHiddenCommandLineKey(ConsoleKeyInfo key)
    {
        if (TryHandlePanelVisibilityFunctionKey(key, out bool shouldRender))
            return shouldRender;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.A, '\u0001'))
        {
            _context.CommandLine.SelectAll();
            return true;
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.C, '\u0003'))
            return _context.CopyCommandLineSelection();

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.V, '\u0016'))
            return _context.PasteTextIntoCommandLine();

        if (TryHandleCommandLineNavigationKey(key, forceCommandLine: true))
            return true;

        switch (key.Key)
        {
            case ConsoleKey.Delete:
                _context.ResetCommandHistoryNavigation();
                _context.CommandLine.DeleteForward();
                return true;

            case ConsoleKey.Backspace:
                _context.ResetCommandHistoryNavigation();
                _context.CommandLine.DeleteBack();
                return true;

            case ConsoleKey.Escape:
                _context.ResetCommandHistoryNavigation();
                _context.CommandLine.Clear();
                return true;

            case ConsoleKey.Enter:
                _context.ResetCommandHistoryNavigation();
                if (_context.CommandLine.HasText)
                    _context.ExecuteCommand(_context.CommandLine.Text);
                return true;

            case ConsoleKey.UpArrow:
                return _context.BrowseCommandHistory(-1, CommandHistoryNavigationStart.Newest);

            case ConsoleKey.DownArrow:
                return _context.BrowseCommandHistory(+1, CommandHistoryNavigationStart.Oldest);

            case ConsoleKey.F10:
                _context.SetRunning(false);
                return false;
        }

        bool isPrintable = key.KeyChar >= ' ' &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (isPrintable)
        {
            _context.ResetCommandHistoryNavigation();
            _context.CommandLine.Insert(key.KeyChar);
            return true;
        }

        return false;
    }

    private static PanelSide OtherPanelSide(PanelSide side) =>
        side == PanelSide.Left ? PanelSide.Right : PanelSide.Left;

    private static string QuoteCommandLineInsertion(string text) =>
        text.Contains(' ') ? $"\"{text}\"" : text;
}
