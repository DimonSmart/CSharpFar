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
        var otherState = PanelSide == PanelSide.Left ? context.RightPanel : context.LeftPanel;
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
        foreach (var moduleItem in context.ModuleDiskMenuItems)
        {
            items.Add(new VolumeSelectionItem
            {
                Label = moduleItem.Text,
                Shortcut = moduleItem.HotKey?.ToString(),
                Action = VolumeSelectionAction.OpenModule,
                ModuleActionId = moduleItem.ActionId,
                ModulePanelSide = PanelSide,
            });
        }

        int initialCursor = FindInitialCursor(items, targetState.CurrentDirectory);

        var selected = new DriveDialog(context.ModalDialogs, context.Palette).Show(items, initialCursor);
        if (selected is null)
            return ApplicationCommandResult.Rendered();

        if (selected.Action == VolumeSelectionAction.OpenModule)
        {
            if (selected.ModuleActionId is { } actionId)
                return context.OpenModuleDiskMenuItem(actionId, selected.ModulePanelSide ?? PanelSide);
            return ApplicationCommandResult.Rendered();
        }

        var volume = selected.Volume!;
        string directoryPath = SelectedVolumeDirectory(volume.RootPath, otherState.CurrentDirectory);

        context.QuickView = false;
        context.ActiveSide = PanelSide;

        if (context.Controller.TryLoadDirectory(targetState, directoryPath, context.PanelOptions))
        {
            context.History.AddDirectory(new DirectoryHistoryItem { Path = directoryPath });
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

    private static string SelectedVolumeDirectory(string rootPath, string otherPanelDirectory)
    {
        if (IsSameVolumePath(rootPath, otherPanelDirectory))
            return otherPanelDirectory;

        return rootPath;
    }

    private static bool IsSameVolumePath(string rootPath, string directory)
    {
        string normalizedRoot = NormalizeDirectoryPrefix(rootPath);
        string normalizedDirectory = NormalizeDirectoryPrefix(directory);
        return normalizedDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPrefix(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return Path.EndsInDirectorySeparator(fullPath)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

}
