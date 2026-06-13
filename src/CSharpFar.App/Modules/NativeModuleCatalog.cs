using CSharpFar.Core.Models;
using CSharpFar.Module.Abstractions;

namespace CSharpFar.App.Modules;

internal sealed class NativeModuleCatalog
{
    private readonly List<ModuleMenuProjection> _menuItems = [];
    private readonly List<ModuleMenuProjection> _diskMenuItems = [];
    private readonly Dictionary<Guid, Func<PanelSide, ModuleActionResult>> _menuActions = [];
    private readonly Dictionary<Guid, Func<PanelSide, ModuleActionResult>> _diskMenuActions = [];
    private readonly Dictionary<string, Func<string, PanelSide, ModuleActionResult>> _commandActions =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ModuleMenuProjection> MenuItems => _menuItems;

    public IReadOnlyList<ModuleMenuProjection> DiskMenuItems => _diskMenuItems;

    public IReadOnlyCollection<string> CommandPrefixes => _commandActions.Keys;

    public void AddMenuAction(
        ModuleMenuProjection item,
        Func<PanelSide, ModuleActionResult> action) =>
        AddAction(_menuItems, _menuActions, item, action, "menu");

    public void AddDiskMenuAction(
        ModuleMenuProjection item,
        Func<PanelSide, ModuleActionResult> action) =>
        AddAction(_diskMenuItems, _diskMenuActions, item, action, "disk menu");

    public void AddCommandPrefix(
        string prefix,
        Func<string, PanelSide, ModuleActionResult> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(action);

        if (_commandActions.ContainsKey(prefix))
            throw new InvalidOperationException($"Duplicate native module command prefix '{prefix}'.");

        _commandActions.Add(prefix, action);
    }

    public ModuleActionResult OpenFromMenu(Guid actionId, PanelSide panelSide) =>
        _menuActions.TryGetValue(actionId, out var action)
            ? action(panelSide)
            : ModuleActionResult.Failed($"Module menu action '{actionId:D}' is not registered.");

    public ModuleActionResult OpenFromDiskMenu(Guid actionId, PanelSide panelSide) =>
        _diskMenuActions.TryGetValue(actionId, out var action)
            ? action(panelSide)
            : ModuleActionResult.Failed($"Module disk action '{actionId:D}' is not registered.");

    public bool TryOpenFromCommandLine(
        string commandLine,
        PanelSide panelSide,
        out ModuleActionResult result)
    {
        string prefix = GetCommandPrefix(commandLine);
        if (!_commandActions.TryGetValue(prefix, out var action))
        {
            result = ModuleActionResult.NoPanel();
            return false;
        }

        result = action(commandLine, panelSide);
        return true;
    }

    private static void AddAction(
        List<ModuleMenuProjection> items,
        Dictionary<Guid, Func<PanelSide, ModuleActionResult>> actions,
        ModuleMenuProjection item,
        Func<PanelSide, ModuleActionResult> action,
        string actionKind)
    {
        if (actions.ContainsKey(item.ActionId))
            throw new InvalidOperationException($"Duplicate native module {actionKind} action '{item.ActionId:D}'.");

        items.Add(item);
        actions.Add(item.ActionId, action);
    }

    private static string GetCommandPrefix(string commandLine)
    {
        int index = 0;
        while (index < commandLine.Length &&
               commandLine[index] != ':' &&
               !char.IsWhiteSpace(commandLine[index]))
        {
            index++;
        }

        return commandLine[..index];
    }
}
