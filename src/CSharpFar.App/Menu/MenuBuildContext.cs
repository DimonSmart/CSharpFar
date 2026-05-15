using CSharpFar.App.Plugins;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Menu;

public sealed record MenuBuildContext
{
    public required PanelSide ActivePanelSide { get; init; }
    public required FilePanelState LeftPanel { get; init; }
    public required FilePanelState RightPanel { get; init; }
    public required PanelViewMode LeftViewMode { get; init; }
    public required PanelViewMode RightViewMode { get; init; }
    public required AppSettingsAlias Settings { get; init; }
    public required bool CanSaveSettings { get; init; }
    public IReadOnlyList<PluginMenuProjection> PluginMenuItems { get; init; } = [];
}
