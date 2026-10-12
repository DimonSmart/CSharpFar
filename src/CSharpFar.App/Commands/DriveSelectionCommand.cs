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
        try { return ExecuteCore(context); }
        finally { context.ResetFunctionKeyLayer(); }
    }

    private ApplicationCommandResult ExecuteCore(ApplicationCommandContext context)
    {
        var targetState = PanelSide == PanelSide.Left ? context.LeftPanel : context.RightPanel;
        var otherState = PanelSide == PanelSide.Left ? context.RightPanel : context.LeftPanel;
        var volumes = context.VolumeService?.GetVolumes() ?? [];
        var items = volumes
            .Select(v => new VolumeSelectionItem
            {
                Label = v.DisplayName, Shortcut = v.Shortcut, Volume = v,
                Action = VolumeSelectionAction.OpenVolume,
            }).ToList();
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

        int initialCursor = FindInitialCursor(items, targetState.CurrentDirectory, targetState.SourceId);
        bool mac = OperatingSystem.IsMacOS() && targetState.SourceId != PanelSourceId.Demo;
        var selected = new DriveDialog(context.ModalDialogs, context.Dialogs).Show(items, initialCursor, mac);
        if (selected is null) return ApplicationCommandResult.Rendered();

        if (selected.Action == VolumeSelectionAction.OpenModule)
        {
            if (selected.ModuleActionId is { } actionId)
                return context.OpenModuleDiskMenuItem(actionId, selected.ModulePanelSide ?? PanelSide);
            return ApplicationCommandResult.Rendered();
        }

        var volume = selected.Volume!;
        string directoryPath = SelectedVolumeDirectory(volume.RootPath, otherState.CurrentDirectory,
            otherState.SourceId, volumes);

        // Do not change the workspace, quick view, history or watcher before the load succeeds.
        PanelLocation destination = targetState.SourceId == PanelSourceId.Demo
            ? new PanelLocation(PanelSourceId.Demo, directoryPath)
            : PanelLocation.Local(directoryPath);
        bool loaded = context.Controller.TryLoadLocation(targetState,
            destination, context.PanelOptions, preserveOnError: true);
        if (!loaded)
        {
            context.Dialogs.Message("Change drive", $"Unable to open volume: {volume.RootPath}");
            return ApplicationCommandResult.Rendered();
        }

        context.QuickView = false;
        context.ActiveSide = PanelSide;
        if (destination.SourceId == PanelSourceId.Local)
            context.History.AddDirectory(new DirectoryHistoryItem { Path = directoryPath });
        context.StartWatching(targetState, PanelSide);
        return ApplicationCommandResult.Rendered();
    }

    internal static int FindInitialCursor(IReadOnlyList<VolumeSelectionItem> items, string currentDirectory, PanelSourceId sourceId)
    {
        if (sourceId != PanelSourceId.Local)
        {
            int rootIndex = Enumerable.Range(0, items.Count)
                .FirstOrDefault(i => items[i].Volume?.RootPath == "/", -1);
            return Math.Max(0, rootIndex);
        }

        int bestIndex = 0;
        int bestLength = -1;
        for (int i = 0; i < items.Count; i++)
        {
            string? root = items[i].Volume?.RootPath;
            if (root is null || !ContainsPath(root, currentDirectory) || root.Length <= bestLength) continue;
            bestLength = root.Length;
            bestIndex = i;
        }
        return bestIndex;
    }

    internal static string SelectedVolumeDirectory(
        string rootPath, string otherPanelDirectory, PanelSourceId otherSource,
        IReadOnlyList<FileSystemVolume> volumes)
    {
        if (otherSource != PanelSourceId.Local)
            return rootPath;

        FileSystemVolume? owning = volumes
            .Where(volume => ContainsPath(volume.RootPath, otherPanelDirectory))
            .OrderByDescending(volume => Normalize(volume.RootPath).Length)
            .FirstOrDefault();

        return owning is not null &&
            string.Equals(Normalize(owning.RootPath), Normalize(rootPath), Comparison)
            ? otherPanelDirectory : rootPath;
    }

    internal static bool ContainsPath(string rootPath, string directory)
    {
        string root = Normalize(rootPath);
        string path = Normalize(directory);
        if (string.Equals(root, path, Comparison)) return true;
        if (!path.StartsWith(root, Comparison)) return false;
        return Path.EndsInDirectorySeparator(root) ||
            (path.Length > root.Length && Path.DirectorySeparatorChar == path[root.Length]) ||
            (path.Length > root.Length && Path.AltDirectorySeparatorChar == path[root.Length]);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
