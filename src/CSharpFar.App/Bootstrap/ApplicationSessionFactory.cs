using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal sealed record ApplicationInitialSession(
    FilePanelState LeftPanel,
    FilePanelState RightPanel,
    PanelViewMode LeftViewMode,
    PanelViewMode RightViewMode);

internal static class ApplicationSessionFactory
{
    public static ApplicationInitialSession Create(
        AppSettingsAlias settings,
        PanelController controller)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var leftStart = ResolveStartDir(settings.Panels.LeftStartDirectory, currentDirectory);
        var rightStart = ResolveStartDir(settings.Panels.RightStartDirectory, currentDirectory);
        var sortMode = ResolveSortMode(settings.Panels.DefaultSortMode);

        var left = new FilePanelState { CurrentDirectory = leftStart, SortMode = sortMode };
        var right = new FilePanelState { CurrentDirectory = rightStart, SortMode = sortMode };
        var options = settings.Panels.Options;
        controller.LoadDirectory(left, leftStart, options);
        controller.LoadDirectory(right, rightStart, options);

        return new ApplicationInitialSession(
            left,
            right,
            ResolveViewMode(settings.Panels.LeftViewMode),
            ResolveViewMode(settings.Panels.RightViewMode));
    }

    private static string ResolveStartDir(string? configured, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;
        return fallback;
    }

    private static SortMode ResolveSortMode(string? configured) =>
        Enum.TryParse<SortMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : SortMode.Name;

    private static PanelViewMode ResolveViewMode(string? configured) =>
        Enum.TryParse<PanelViewMode>(configured, ignoreCase: true, out var mode)
            ? mode
            : PanelViewMode.Full;
}
