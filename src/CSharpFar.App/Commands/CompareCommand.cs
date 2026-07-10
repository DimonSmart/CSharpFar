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

internal sealed class CompareCommand : IApplicationCommand
{
    private readonly CompareCommandKind _kind;

    public CompareCommand(CompareCommandKind kind)
    {
        _kind = kind;
    }

    public string CommandId => _kind == CompareCommandKind.FileSets
        ? ApplicationCommandIds.CompareFileSets
        : ApplicationCommandIds.CompareFolders;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) =>
        context.ActiveState.SourceId == PanelSourceId.Local &&
        context.PassiveState.SourceId == PanelSourceId.Local;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        string title = _kind == CompareCommandKind.FileSets ? "Compare file sets" : "Compare folders";
        if (!CanExecute(context, args))
        {
            context.ShowMessage(title, "Comparison is only supported for local panels.");
            return ApplicationCommandResult.Rendered();
        }

        if (!Directory.Exists(context.ActiveState.CurrentDirectory) ||
            !Directory.Exists(context.PassiveState.CurrentDirectory))
        {
            context.ShowMessage(title, "Both panels must point to existing directories.");
            return ApplicationCommandResult.Rendered();
        }

        CompareMode mode = _kind == CompareCommandKind.FileSets ? CompareMode.FileSet : CompareMode.FolderStructure;
        var options = new CompareOptionsDialog(context.ModalDialogs).Show(
            mode,
            context.Settings.Compare,
            context.LeftPanel,
            context.RightPanel);
        if (options is null)
            return ApplicationCommandResult.Rendered();

        StoreOptions(context.Settings.Compare, options);
        context.SaveSettings();

        try
        {
            var left = BuildScanRequest(context.LeftPanel, context.RightPanel, options.SelectedItemsOnly);
            var right = BuildScanRequest(context.RightPanel, context.LeftPanel, options.SelectedItemsOnly);
            CompareResult result = mode == CompareMode.FileSet
                ? new FileSetCompareEngine().Compare(left, right, options)
                : new FolderStructureCompareEngine().Compare(left, right, options);

            ComparisonSelectionApplier.Apply(result, context.LeftPanel, context.RightPanel);
            new CompareSummaryDialog(context.ModalDialogs).Show(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or ArgumentException)
        {
            context.ShowMessage(title, ex.Message);
        }
        finally
        {
            context.ResetFunctionKeyLayer();
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
        state.SelectedPaths.ToList();

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
        settings.TimestampTolerance = options.TimestampTolerance.ToString();
        settings.NameComparison = options.NameComparison.ToString();
        settings.FileSetMatchMode = options.FileSetMatchMode.ToString();
        settings.SelectedItemsOnly = options.SelectedItemsOnly;
    }
}
