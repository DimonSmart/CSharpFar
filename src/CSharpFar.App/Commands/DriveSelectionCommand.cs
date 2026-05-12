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

        int initialCursor = FindInitialCursor(items, targetState.CurrentDirectory);

        var selected = new DriveDialog(context.Screen, context.Palette).Show(items, initialCursor);
        if (selected is null)
            return ApplicationCommandResult.Rendered();

        var volume = selected.Volume!;

        try
        {
            context.Controller.LoadDirectory(targetState, volume.RootPath, context.PanelOptions);
            context.History.AddDirectory(new DirectoryHistoryItem { Path = volume.RootPath });
            context.QuickView = false;
            context.ActiveSide = PanelSide;
            context.StartWatching(targetState, PanelSide);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            new MessageDialog(context.Screen, context.Palette).Show("Change drive", ex.Message);
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
