using CSharpFar.Plugin.Abstractions;

namespace CSharpFar.App.Plugins;

internal sealed class PluginManager
{
    private readonly Dictionary<Guid, PluginRegistration> _plugins = new();
    private readonly List<PluginMenuProjection> _pluginMenuItems = [];
    private readonly List<PluginMenuProjection> _diskMenuItems = [];

    public PluginManager(
        IEnumerable<ICSharpFarPlugin> plugins,
        PluginStartupInfo startupInfo)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(startupInfo);

        foreach (var plugin in plugins)
            Register(plugin, startupInfo);
    }

    public IReadOnlyList<PluginMenuProjection> PluginMenuItems => _pluginMenuItems;

    public IReadOnlyList<PluginMenuProjection> DiskMenuItems => _diskMenuItems;

    public PluginOpenResult OpenFromPluginMenu(Guid pluginId, Guid itemId)
    {
        if (!TryGetRegistration(pluginId, out var registration))
            return PluginOpenResult.Failed($"Plugin '{pluginId}' is not registered.");

        if (!registration.PluginMenuItemIds.Contains(itemId))
            return PluginOpenResult.Failed($"Plugin menu item '{itemId}' is not registered.");

        return registration.Plugin.Open(new PluginOpenInfo
        {
            OpenFrom = PluginOpenFrom.PluginMenu,
            SelectedItemId = itemId,
        });
    }

    public PluginOpenResult OpenFromDiskMenu(Guid pluginId, Guid itemId, PluginOpenFrom openFrom)
    {
        if (!TryGetRegistration(pluginId, out var registration))
            return PluginOpenResult.Failed($"Plugin '{pluginId}' is not registered.");

        if (!registration.DiskMenuItemIds.Contains(itemId))
            return PluginOpenResult.Failed($"Plugin disk menu item '{itemId}' is not registered.");

        return registration.Plugin.Open(new PluginOpenInfo
        {
            OpenFrom = openFrom,
            SelectedItemId = itemId,
            PanelSide = openFrom == PluginOpenFrom.LeftDiskMenu
                ? Core.Models.PanelSide.Left
                : Core.Models.PanelSide.Right,
        });
    }

    private void Register(ICSharpFarPlugin plugin, PluginStartupInfo startupInfo)
    {
        var globalInfo = plugin.GetGlobalInfo();
        if (!_plugins.TryAdd(globalInfo.PluginId, new PluginRegistration(plugin, [], [])))
            throw new InvalidOperationException($"Duplicate plugin id: {globalInfo.PluginId}.");

        plugin.SetStartupInfo(startupInfo);

        var pluginInfo = plugin.GetPluginInfo();
        ValidateDuplicateItems(globalInfo.PluginId, "plugin", pluginInfo.PluginMenuItems);
        ValidateDuplicateItems(globalInfo.PluginId, "disk", pluginInfo.DiskMenuItems);
        ValidateDuplicateItems(globalInfo.PluginId, "config", pluginInfo.ConfigMenuItems);

        _plugins[globalInfo.PluginId] = new PluginRegistration(
            plugin,
            pluginInfo.PluginMenuItems.Select(item => item.ItemId).ToHashSet(),
            pluginInfo.DiskMenuItems.Select(item => item.ItemId).ToHashSet());

        _pluginMenuItems.AddRange(pluginInfo.PluginMenuItems.Select(item =>
            new PluginMenuProjection(globalInfo.PluginId, item.ItemId, item.Text, item.HotKey)));
        _diskMenuItems.AddRange(pluginInfo.DiskMenuItems.Select(item =>
            new PluginMenuProjection(globalInfo.PluginId, item.ItemId, item.Text, item.HotKey)));
    }

    private bool TryGetRegistration(Guid pluginId, out PluginRegistration registration) =>
        _plugins.TryGetValue(pluginId, out registration!);

    private static void ValidateDuplicateItems(
        Guid pluginId,
        string menuName,
        IReadOnlyList<PluginMenuItem> items)
    {
        var duplicate = items
            .GroupBy(item => item.ItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginId}' has duplicate {menuName} item id '{duplicate.Key}'.");
        }
    }

    private sealed record PluginRegistration(
        ICSharpFarPlugin Plugin,
        HashSet<Guid> PluginMenuItemIds,
        HashSet<Guid> DiskMenuItemIds);
}
