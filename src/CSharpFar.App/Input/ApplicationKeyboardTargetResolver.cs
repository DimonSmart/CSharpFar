using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Input;

internal sealed class ApplicationKeyboardTargetResolver
{
    private readonly KeyboardInputContext _context;

    public ApplicationKeyboardTargetResolver(KeyboardInputContext context)
    {
        _context = context;
    }

    public UiTargetId? Resolve(ConsoleKeyInfo key, ApplicationUiFrame frame)
    {
        if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine)
            return ApplicationTargetIds.CommandLine;

        if (frame.Mode != ApplicationWorkspaceMode.Panels)
            return null;

        if (CommandLineOwns(key))
            return ApplicationTargetIds.CommandLine;

        if (PanelOwns(key))
            return ApplicationTargetIds.Panel(_context.ActiveSide());

        return null;
    }

    private bool CommandLineOwns(ConsoleKeyInfo key)
    {
        if (IsPrintable(key))
            return true;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.A, '\u0001') ||
            KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.C, '\u0003') ||
            KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.V, '\u0016') ||
            KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.E, '\u0005') ||
            KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.X, '\u0018') ||
            KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.F, '\u0006') ||
            KeyboardShortcutClassifier.IsPlainControlEnter(key) ||
            KeyboardShortcutClassifier.IsPlainControlOpenBracket(key) ||
            KeyboardShortcutClassifier.IsPlainControlCloseBracket(key))
        {
            return true;
        }

        bool hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        bool hasControl = (key.Modifiers & ConsoleModifiers.Control) != 0;
        bool hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        bool hasTextOrSelection = _context.CommandLine.HasText || _context.CommandLine.HasSelection;

        return key.Key switch
        {
            ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Home or ConsoleKey.End
                when !hasAlt && (hasTextOrSelection || hasControl || hasShift) => true,
            ConsoleKey.Delete => true,
            ConsoleKey.Backspace when _context.CommandLine.HasText => true,
            ConsoleKey.Escape when _context.ActiveState().SearchRequest is null => true,
            ConsoleKey.Enter when _context.CommandLine.HasText => true,
            _ => false,
        };
    }

    private bool PanelOwns(ConsoleKeyInfo key)
    {
        if (!_context.IsPanelsMode())
            return false;

        if (key.Modifiers == 0)
        {
            return key.Key is
                ConsoleKey.LeftArrow or
                ConsoleKey.RightArrow or
                ConsoleKey.Home or
                ConsoleKey.End or
                ConsoleKey.Backspace or
                ConsoleKey.Escape or
                ConsoleKey.Enter or
                ConsoleKey.Insert or
                ConsoleKey.Tab or
                ConsoleKey.UpArrow or
                ConsoleKey.DownArrow or
                ConsoleKey.PageUp or
                ConsoleKey.PageDown;
        }

        bool isControlShortcut =
            (key.Modifiers & ConsoleModifiers.Control) != 0 &&
            (key.Modifiers & ConsoleModifiers.Alt) == 0;
        return isControlShortcut && key.Key is ConsoleKey.Multiply or ConsoleKey.D8;
    }

    internal static bool IsPrintable(ConsoleKeyInfo key) =>
        key.KeyChar >= ' ' &&
        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0 &&
        key.Key is not (
            ConsoleKey.LeftArrow or
            ConsoleKey.RightArrow or
            ConsoleKey.UpArrow or
            ConsoleKey.DownArrow or
            ConsoleKey.Home or
            ConsoleKey.End or
            ConsoleKey.PageUp or
            ConsoleKey.PageDown or
            ConsoleKey.Insert or
            ConsoleKey.Delete or
            ConsoleKey.Enter or
            ConsoleKey.Tab or
            ConsoleKey.Backspace or
            ConsoleKey.Escape) &&
        key.Key is < ConsoleKey.F1 or > ConsoleKey.F24;
}
