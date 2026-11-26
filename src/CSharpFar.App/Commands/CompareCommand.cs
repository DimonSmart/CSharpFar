using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;
using CSharpFar.Core.Services;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Abstractions;
using CSharpFar.App.UserMenu;
using CSharpFar.App.State;
using CSharpFar.App.Rendering;
using CSharpFar.App.Panels;
using CSharpFar.App.Modules;
using CSharpFar.App.CommandLine;
using CSharpFar.App.AutoRefresh;
using CSharpFar.App.Dialogs;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Commands;

internal enum CompareCommandKind
{
    Folders,
    FileSets,
}

internal sealed class CompareCommand(PanelWorkspaceController workspace, PanelSessionState panels, FilePanelSourceRegistry sourceRegistry, DialogService dialogs, FormFieldFactory fields, AppSettingsAlias settings, PanelController controller, Action? saveSettings, Action resetFunctionKeyLayer, CompareCommandKind kind) : IApplicationCommand
{
    public string CommandId => kind == CompareCommandKind.FileSets
        ? ApplicationCommandIds.CompareFileSets
        : ApplicationCommandIds.CompareFolders;

    public bool CanExecute(object? args = null) =>
        PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, workspace.ActiveState) &&
        PanelCommandTargetResolver.CanAccessLocalFileSystem(sourceRegistry, workspace.PassiveState);

    public ApplicationCommandResult Execute(object? args = null)
    {
        string title = kind == CompareCommandKind.FileSets ? "Compare file sets" : "Compare folders";
        if (!CanExecute(args))
        {
            dialogs.Message(title, "Comparison is only supported for local panels.");
            return ApplicationCommandResult.Rendered();
        }

        if (!Directory.Exists(workspace.ActiveState.CurrentDirectory) ||
            !Directory.Exists(workspace.PassiveState.CurrentDirectory))
        {
            dialogs.Message(title, "Both panels must point to existing directories.");
            return ApplicationCommandResult.Rendered();
        }

        CompareMode mode = kind == CompareCommandKind.FileSets ? CompareMode.FileSet : CompareMode.FolderStructure;
        var options = new CompareOptionsDialog(dialogs, fields).Show(
            mode,
            settings.Compare,
            panels.Left,
            panels.Right);
        if (options is null)
            return ApplicationCommandResult.Rendered();

        StoreOptions(settings.Compare, options);
        saveSettings?.Invoke();

        try
        {
            var left = BuildScanRequest(panels.Left, panels.Right, options.SelectedItemsOnly);
            var right = BuildScanRequest(panels.Right, panels.Left, options.SelectedItemsOnly);
            CompareResult result = mode == CompareMode.FileSet
                ? new FileSetCompareEngine().Compare(left, right, options)
                : new FolderStructureCompareEngine().Compare(left, right, options);

            ComparisonSelectionApplier.Apply(result, panels.Left, panels.Right, controller);
            new CompareSummaryDialog(dialogs).Show(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
        {
            dialogs.Message(title, ex.Message);
        }
        finally
        {
            resetFunctionKeyLayer();
        }

        return ApplicationCommandResult.Rendered();
    }

    private static FolderScanRequest BuildScanRequest(
        FilePanelState state,
        FilePanelState opposite,
        bool selectedItemsOnly)
    {
        if (!selectedItemsOnly)
            return new FolderScanRequest { RootPath = state.CurrentDirectory };

        var selected = ExistingSelectedPaths(state);
        if (selected.Count > 0)
            return new FolderScanRequest { RootPath = state.CurrentDirectory, SelectedPaths = selected };

        var oppositeSelected = ExistingSelectedPaths(opposite);
        if (oppositeSelected.Count == 0)
            return new FolderScanRequest { RootPath = state.CurrentDirectory };

        var corresponding = oppositeSelected
            .Select(path => Path.Combine(state.CurrentDirectory, Path.GetFileName(path)))
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .ToList();

        return corresponding.Count > 0
            ? new FolderScanRequest { RootPath = state.CurrentDirectory, SelectedPaths = corresponding }
            : new FolderScanRequest { RootPath = state.CurrentDirectory };
    }

    private static IReadOnlyList<string> ExistingSelectedPaths(FilePanelState state) =>
        state.SelectedLocations.Where(location => location.SourceId == PanelSourceId.Local).Select(location => location.SourcePath).ToList();

    private static void StoreOptions(AppSettings.CompareSettings settings, ComparisonOptions options)
    {
        settings.Mode = options.Mode.ToString();
        settings.IncludeSubfolders = options.IncludeSubfolders;
        settings.Depth = options.MaxDepth switch
        {
            null => "All",
            0 => "0",
            1 => "1",
            2 => "2",
            _ => "Custom",
        };
        settings.CustomDepth = options.MaxDepth ?? settings.CustomDepth;
        settings.IncludeMasks = options.IncludeMasks;
        settings.ExcludeMasks = options.ExcludeMasks;
        settings.Method = options.Method.ToString();
        settings.TextLineEndings = options.TextLineEndings.ToString();
        settings.TextWhitespace = options.TextWhitespace.ToString();
        settings.TextBom = options.TextBom.ToString();
        settings.TimestampTolerance = options.TimestampTolerance.ToString();
        settings.NameComparison = options.NameComparison.ToString();
        settings.FileSetMatchMode = options.FileSetMatchMode.ToString();
        settings.SelectedItemsOnly = options.SelectedItemsOnly;
    }
}
