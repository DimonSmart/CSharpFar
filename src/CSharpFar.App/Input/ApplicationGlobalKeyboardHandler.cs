using CSharpFar.App.Commands;
using CSharpFar.App.DirectoryShortcuts;
using CSharpFar.App.FunctionKeys;
using CSharpFar.App.Panels;
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

    public ApplicationInputHandlingResult Handle(ConsoleKeyInfo key)
    {
        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.O, '\u000f'))
            return Handled(_context.ExecuteRegisteredCommand(ApplicationCommandIds.TogglePanels, null));

        if (!_context.IsPanelsMode())
            return ApplicationInputHandlingResult.NotHandled;

        if (TryHandleDirectoryShortcut(key))
            return ApplicationInputHandlingResult.FromHandled(true);

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
            return Handled(_context.ExecuteRegisteredCommand(MenuCommandIds.SettingsOpenPanelSettings, null));

        if (KeyboardShortcutClassifier.IsPlainControlBackslash(key))
            return Handled(_context.ExecuteRegisteredCommand(ApplicationCommandIds.NavigateToRoot, null));

        if ((key.Modifiers & ConsoleModifiers.Alt) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Shift)) == 0)
        {
            if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1)
            {
                return Handled(_context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _context.ActiveSide(),
                        ViewMode = PanelViewMode.Full,
                    }));
            }

            if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2)
            {
                return Handled(_context.ExecuteRegisteredCommand(
                    MenuCommandIds.PanelSetViewMode,
                    new SetPanelViewModeArgs
                    {
                        PanelSide = _context.ActiveSide(),
                        ViewMode = PanelViewMode.BriefTwoColumns,
                    }));
            }
        }

        if (KeyboardShortcutClassifier.IsPlainControlKey(key, ConsoleKey.Q, '\u0011'))
        {
            _context.SetQuickView(!_context.QuickView());
            return ApplicationInputHandlingResult.FromHandled(true);
        }

        if (TryHandleFunctionKey(key, out bool functionKeyShouldRender))
            return ApplicationInputHandlingResult.FromHandled(functionKeyShouldRender);

        return ApplicationInputHandlingResult.NotHandled;
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

    private static ApplicationInputHandlingResult Handled(bool shouldRender) =>
        ApplicationInputHandlingResult.FromHandled(shouldRender);
}
