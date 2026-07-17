using CSharpFar.App.Commands;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Input;

internal sealed class ApplicationGlobalKeyboardHandler
{
    private readonly KeyboardInputContext _context;

    public ApplicationGlobalKeyboardHandler(KeyboardInputContext context)
    {
        _context = context;
    }

    public ApplicationInputHandlingResult Handle(ApplicationKeyboardInput input)
    {
        ConsoleKeyInfo key = input.Key;
        ApplicationUiFrame frame = input.Frame;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return Handled(_context.ExecuteRegisteredCommand(ApplicationCommandIds.TogglePanels, null));

        if (frame.Mode != ApplicationWorkspaceMode.Panels)
            return ApplicationInputHandlingResult.NotHandled;

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
            return Handled(_context.ExecuteRegisteredCommand(MenuCommandIds.SettingsOpenPanelSettings, null));

        if (KeyboardShortcutClassifier.IsPlainControlBackslash(key))
        {
            ApplicationPanelFrame? panel = input.ActivePanelFrame;
            if (panel is null)
                return ApplicationInputHandlingResult.NotHandled;

            return Handled(_context.ExecuteRegisteredCommand(
                ApplicationCommandIds.NavigateToRoot,
                new NavigateToRootArgs(input.ActiveSide, panel.Keyboard.CurrentDirectory)));
        }

        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                return Handled(_context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = frame.Keyboard.ActiveSide,
                        ViewMode = PanelViewMode.Full,
                    }));
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                return Handled(_context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = frame.Keyboard.ActiveSide,
                        ViewMode = PanelViewMode.BriefTwoColumns,
                    }));
            }
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.Q, '\u0011'))
        {
            _context.SetQuickView(!_context.QuickView());
            return ApplicationInputHandlingResult.FromHandled(true);
        }

        return ApplicationInputHandlingResult.NotHandled;
    }

    private static ApplicationInputHandlingResult Handled(bool shouldRender) =>
        ApplicationInputHandlingResult.FromHandled(shouldRender);
}
