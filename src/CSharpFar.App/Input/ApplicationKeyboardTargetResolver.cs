using CSharpFar.App.CommandLine;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationKeyboardTargetResolver
{
    public ApplicationKeyboardOwner Resolve(ConsoleKeyInfo key, ApplicationUiFrame frame)
    {
        if (frame.Mode == ApplicationWorkspaceMode.HiddenCommandLine)
            return ApplicationKeyboardOwner.CommandLine;

        if (frame.Mode != ApplicationWorkspaceMode.Panels)
            return ApplicationKeyboardOwner.None;

        if (CommandLineOwns(key, frame.Keyboard))
            return ApplicationKeyboardOwner.CommandLine;

        if (PanelOwns(key, frame))
        {
            return frame.Keyboard.ActiveSide == PanelSide.Left
                ? ApplicationKeyboardOwner.LeftPanel
                : ApplicationKeyboardOwner.RightPanel;
        }

        return ApplicationKeyboardOwner.None;
    }

    private static bool CommandLineOwns(ConsoleKeyInfo key, ApplicationKeyboardFrame keyboard)
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
        bool hasTextOrSelection = keyboard.CommandLineHasText || keyboard.CommandLineHasSelection;

        return key.Key switch
        {
            ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Home or ConsoleKey.End
                when !hasAlt && (hasTextOrSelection || hasControl || hasShift) => true,
            ConsoleKey.Delete => true,
            ConsoleKey.Backspace when keyboard.CommandLineHasText => true,
            ConsoleKey.Escape when !keyboard.ActivePanelHasSearchRequest => true,
            ConsoleKey.Enter when keyboard.CommandLineHasText => true,
            _ => false,
        };
    }

    private static bool PanelOwns(ConsoleKeyInfo key, ApplicationUiFrame frame)
    {
        ApplicationPanelFrame? panel = frame.Keyboard.ActiveSide == PanelSide.Left
            ? frame.LeftPanel
            : frame.RightPanel;
        if (panel is null)
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

internal enum ApplicationKeyboardOwner
{
    None,
    CommandLine,
    LeftPanel,
    RightPanel,
}
