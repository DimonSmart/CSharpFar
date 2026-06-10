using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FarNetHost;
using CSharpFar.Module.Abstractions;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Modules;

internal sealed class FarNetPanelActionService
{
    private readonly FilePanelSourceRegistry _sourceRegistry;
    private readonly PanelController _controller;
    private readonly ScreenRenderer _screen;
    private readonly Func<ConsolePalette> _palette;
    private readonly AppSettingsAlias _settings;
    private readonly ITextClipboard _clipboard;
    private readonly ModulePanelOpener _modulePanelOpener;
    private readonly Func<PanelSide, int> _visibleRows;
    private readonly Func<FilePanelState, PanelSide> _panelSideForState;
    private readonly Action<FilePanelState, int> _safeRefresh;

    public FarNetPanelActionService(
        FilePanelSourceRegistry sourceRegistry,
        PanelController controller,
        ScreenRenderer screen,
        Func<ConsolePalette> palette,
        AppSettingsAlias settings,
        ITextClipboard clipboard,
        ModulePanelOpener modulePanelOpener,
        Func<PanelSide, int> visibleRows,
        Func<FilePanelState, PanelSide> panelSideForState,
        Action<FilePanelState, int> safeRefresh)
    {
        _sourceRegistry = sourceRegistry;
        _controller = controller;
        _screen = screen;
        _palette = palette;
        _settings = settings;
        _clipboard = clipboard;
        _modulePanelOpener = modulePanelOpener;
        _visibleRows = visibleRows;
        _panelSideForState = panelSideForState;
        _safeRefresh = safeRefresh;
    }

    public bool TryHandleShortcut(FilePanelState activeState, bool commandLineHasText, ConsoleKeyInfo key)
    {
        if (!TryGetFarNetPanel(activeState, out var farNetPanel))
            return false;

        if (IsPlainControlKey(key, ConsoleKey.S, '\u0013'))
            return HandleKey(activeState, farNetPanel, key);

        bool shiftOnly = (key.Modifiers & ConsoleModifiers.Shift) != 0 &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;
        if (shiftOnly && key.Key is ConsoleKey.Delete or ConsoleKey.F8)
            return HandleKey(activeState, farNetPanel, key);

        if (!commandLineHasText &&
            (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0 &&
            key.Key == ConsoleKey.Delete)
        {
            return HandleKey(activeState, farNetPanel, key);
        }

        return false;
    }

    public bool TryEditItem(FilePanelState state, FilePanelItem item)
    {
        if (item.IsParentDirectory)
            return false;
        if (!TryGetFarNetPanel(state, out var farNetPanel))
            return false;

        if (!farNetPanel.TryGetEditableText(item.SourcePath, out string text, out string? error))
        {
            new MessageDialog(_screen, _palette()).Show("Edit", error ?? "FarNet panel item cannot be edited.");
            return true;
        }

        string tempDirectory = Path.Combine(Path.GetTempPath(), "CSharpFar", "FarNetEdit", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, GetSafeTempFileName(item.Name));
        try
        {
            File.WriteAllText(tempPath, text);
            new FileEditor(_screen, _palette(), _settings.Editor, _clipboard).Show(tempPath);
            string editedText = File.ReadAllText(tempPath);
            if (string.Equals(editedText, text, StringComparison.Ordinal))
                return true;

            var result = farNetPanel.SetEditedText(item.SourcePath, editedText);
            if (result.Kind == ModuleActionResultKind.Failed)
                new MessageDialog(_screen, _palette()).Show("Edit", result.Message ?? "FarNet panel item edit failed.");
            else
                _safeRefresh(state, _visibleRows(_panelSideForState(state)));

            return true;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, recursive: true); }
            catch { }
        }
    }

    public bool TryOpenItem(FilePanelState state, PanelSide side, FilePanelItem item)
    {
        if (!TryGetFarNetPanel(state, out var farNetPanel))
            return false;

        var result = farNetPanel.OpenItem(item.SourcePath);
        if (result.Kind == ModuleActionResultKind.Completed ||
            result.Kind == ModuleActionResultKind.NoPanel)
        {
            return false;
        }

        _modulePanelOpener.HandleOpenResult(result, side);
        return true;
    }

    private bool HandleKey(FilePanelState activeState, IFarNetPanelOperations farNetPanel, ConsoleKeyInfo key)
    {
        var result = farNetPanel.PressKey(
            _controller.CurrentItem(activeState)?.SourcePath,
            GetVirtualKeyCode(key.Key),
            (key.Modifiers & ConsoleModifiers.Shift) != 0,
            (key.Modifiers & ConsoleModifiers.Control) != 0,
            (key.Modifiers & ConsoleModifiers.Alt) != 0);
        if (result.Kind == ModuleActionResultKind.Failed)
            new MessageDialog(_screen, _palette()).Show("FarNet panel", result.Message ?? "FarNet panel key failed.");
        else
            _safeRefresh(activeState, _visibleRows(_panelSideForState(activeState)));

        return result.Kind != ModuleActionResultKind.NoPanel;
    }

    private bool TryGetFarNetPanel(FilePanelState state, out IFarNetPanelOperations farNetPanel)
    {
        if (_sourceRegistry.TryGetSource(state.SourceId, out var source) &&
            source is IFarNetPanelOperations panel)
        {
            farNetPanel = panel;
            return true;
        }

        farNetPanel = null!;
        return false;
    }

    private static int GetVirtualKeyCode(ConsoleKey key) =>
        key switch
        {
            ConsoleKey.Delete => 46,
            ConsoleKey.F8 => 119,
            ConsoleKey.S => 83,
            _ => (int)key,
        };

    private static bool IsPlainControlKey(ConsoleKeyInfo key, ConsoleKey expectedKey, char expectedChar) =>
        key.Key == expectedKey &&
        key.KeyChar == expectedChar &&
        (key.Modifiers & ConsoleModifiers.Control) != 0 &&
        (key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Shift)) == 0;

    private static string GetSafeTempFileName(string name)
    {
        string fileName = string.IsNullOrWhiteSpace(name) ? "value.json" : name;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        return fileName.Length == 0 ? "value.json" : fileName;
    }
}
