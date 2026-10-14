using System.Security.Cryptography;
using System.Text;
using CSharpFar.App.Plugins;
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
            if (item.Item.Placement == placement && ActionId(item) == actionId)
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
            .Select(item => new ModuleMenuProjection(ActionId(item), item.Item.Title, item.Item.HotKey))
            .ToArray();

    private static Guid ActionId(PluginMenuRegistration registration)
    {
        if (Guid.TryParse(registration.Item.Id, out Guid legacyActionId))
            return legacyActionId;

        // Stable action identity for a plugin-local menu ID without prescribing GUID IDs to plugins.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            registration.PluginId + "/" + registration.Item.Id));
        return new Guid(hash.AsSpan(0, 16));
    }
}
