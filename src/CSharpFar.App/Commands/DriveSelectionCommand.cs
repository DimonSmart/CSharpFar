using CSharpFar.App.Dialogs;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal abstract class DriveSelectionCommand : IApplicationCommand
{
    protected DriveSelectionCommand(string commandId, PanelSide panelSide)
    {
        CommandId = commandId;
        PanelSide = panelSide;
    }

    public string CommandId { get; }

    protected PanelSide PanelSide { get; }

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        try
        {
            return ExecuteCore(context);
        }
        finally
        {
            context.ResetFunctionKeyLayer();
        }
    }

    private ApplicationCommandResult ExecuteCore(ApplicationCommandContext context)
    {
        var targetState = PanelSide == PanelSide.Left ? context.LeftPanel : context.RightPanel;
        var volumes = context.VolumeService?.GetVolumes() ?? [];
        var items = volumes
            .Select(v => new VolumeSelectionItem
            {
                Label = v.DisplayName,
                Shortcut = v.Shortcut,
                Volume = v,
                Action = VolumeSelectionAction.OpenVolume,
            })
            .ToList();
        foreach (var pluginItem in context.PluginDiskMenuItems)
        {
            items.Add(new VolumeSelectionItem
            {
                Label = pluginItem.Text,
                Shortcut = pluginItem.HotKey?.ToString(),
                Action = VolumeSelectionAction.OpenPlugin,
                PluginId = pluginItem.PluginId,
                PluginItemId = pluginItem.ItemId,
                PluginPanelSide = PanelSide,
            });
        }

        int initialCursor = FindInitialCursor(items, targetState.CurrentDirectory);

        var selected = new DriveDialog(context.Screen, context.Palette).Show(items, initialCursor);
        if (selected is null)
            return ApplicationCommandResult.Rendered();

        if (selected.Action == VolumeSelectionAction.OpenPlugin)
        {
            if (selected.PluginId is { } pluginId && selected.PluginItemId is { } itemId)
                return context.OpenPluginDiskMenuItem(pluginId, itemId, selected.PluginPanelSide ?? PanelSide);
            return ApplicationCommandResult.Rendered();
        }

        var volume = selected.Volume!;

        context.QuickView = false;
        context.ActiveSide = PanelSide;

        if (context.Controller.TryLoadDirectory(targetState, volume.RootPath, context.PanelOptions))
        {
            context.History.AddDirectory(new DirectoryHistoryItem { Path = volume.RootPath });
            context.StartWatching(targetState, PanelSide);
        }

        return ApplicationCommandResult.Rendered();
    }

    private static int FindInitialCursor(List<VolumeSelectionItem> items, string currentDirectory)
    {
        int bestIndex = 0;
        int bestLength = -1;

        for (int i = 0; i < items.Count; i++)
        {
            string? root = items[i].Volume?.RootPath;
            if (root is null)
                continue;

            if (currentDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                root.Length > bestLength)
            {
                bestLength = root.Length;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

}
