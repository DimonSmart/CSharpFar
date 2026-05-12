using CSharpFar.App.Dialogs;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal sealed class OpenSettingsCommand : IApplicationCommand
{
    public string CommandId => MenuCommandIds.SettingsOpenPanelSettings;

    public bool CanExecute(ApplicationCommandContext context, object? args = null) => true;

    public ApplicationCommandResult Execute(ApplicationCommandContext context, object? args = null)
    {
        var result = new SettingsDialog(context.Screen).Show(
            context.LeftViewMode,
            context.RightViewMode,
            context.Settings.Ui.Palette,
            context.Settings.Panels.FileHighlighting.Enabled);

        if (result is null)
            return ApplicationCommandResult.Rendered();

        context.LeftViewMode = result.LeftViewMode;
        context.RightViewMode = result.RightViewMode;
        context.Settings.Panels.LeftViewMode = result.LeftViewMode.ToString();
        context.Settings.Panels.RightViewMode = result.RightViewMode.ToString();
        context.Settings.Ui.Palette = result.PaletteName;
        context.Settings.Panels.FileHighlighting.Enabled = result.FileHighlightingEnabled;
        context.CommandPalette = PaletteRegistry.Resolve(result.PaletteName);
        context.HighlightService = context.CreateHighlightService();
        context.Controller.MoveCursor(context.LeftPanel, 0, context.VisibleRows(PanelSide.Left));
        context.Controller.MoveCursor(context.RightPanel, 0, context.VisibleRows(PanelSide.Right));
        context.SaveSettings();
        return ApplicationCommandResult.Rendered();
    }
}
