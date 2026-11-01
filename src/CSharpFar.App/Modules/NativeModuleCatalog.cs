using CSharpFar.App.Plugins;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Modules;

/// <summary>Application menu routing projection; the plugin runtime is the only registration registry.</summary>
internal sealed class NativeModuleCatalog(NativePluginRuntime plugins)
{
    public IReadOnlyList<ModuleMenuProjection> MenuItems =>
        Project(PluginMenuPlacement.Plugins);

    public IReadOnlyList<ModuleMenuProjection> DiskMenuItems =>
        Project(PluginMenuPlacement.Disk);

    public IReadOnlyCollection<string> CommandPrefixes => plugins.CommandPrefixes;

    public bool TryOpenFile(IFilePanelSource source, FilePanelItem item, PanelSide side,
        out ValueTask<PluginFileOpenResult> invocation) =>
        plugins.TryOpenFile(source, item, side, out invocation);

    public ValueTask<PluginCommandResult> OpenFromMenu(Guid actionId, PanelSide panelSide) =>
        InvokeMenu(actionId, PluginMenuPlacement.Plugins, PluginCommandOrigin.PluginsMenu, panelSide);

    public ValueTask<PluginCommandResult> OpenFromDiskMenu(Guid actionId, PanelSide panelSide) =>
        InvokeMenu(actionId, PluginMenuPlacement.Disk, PluginCommandOrigin.DiskMenu, panelSide);

    public bool TryOpenFromCommandLine(
        string commandLine, PanelSide panelSide, out ValueTask<PluginCommandResult> result)
    {
        if (!plugins.TryResolvePrefix(commandLine, out var address, out string arguments))
        {
            result = default;
            return false;
        }

        result = plugins.ExecuteAsync(address, PluginCommandOrigin.CommandLine,
            panelSide, commandLine, arguments);
        return true;
    }

    private ValueTask<PluginCommandResult> InvokeMenu(
        Guid actionId, PluginMenuPlacement placement, PluginCommandOrigin origin, PanelSide side)
    {
        foreach (var item in plugins.MenuItems)
        {
            if (item.Item.Placement == placement && PluginMenuActionIds.Get(item.PluginId, item.Item) == actionId)
            {
                return plugins.ExecuteAsync(
                    new PluginCommandAddress(item.PluginId, item.Item.CommandId), origin, side);
            }
        }

        return ValueTask.FromResult(PluginCommandResult.Failed(
            $"Plugin menu action '{actionId:D}' is not registered."));
    }

    private IReadOnlyList<ModuleMenuProjection> Project(PluginMenuPlacement placement) =>
        plugins.MenuItems
            .Where(item => item.Item.Placement == placement)
            .Select(item => new ModuleMenuProjection(PluginMenuActionIds.Get(item.PluginId, item.Item), item.Item.Title, item.Item.HotKey))
            .ToArray();

}
