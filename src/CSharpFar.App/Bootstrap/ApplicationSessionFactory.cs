using CSharpFar.App.CommandLine;
using CSharpFar.App.State;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationSessionFactory
{
    public static ApplicationSession Create(
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

        return new ApplicationSession
        {
            App = new ApplicationState(PaletteRegistry.Resolve(settings.Ui.Palette)),
            Ui = new UiTransientState(),
            Panels = new PanelSessionState
            {
                Left = left,
                Right = right,
                LeftViewMode = ResolveViewMode(settings.Panels.LeftViewMode),
                RightViewMode = ResolveViewMode(settings.Panels.RightViewMode),
            },
            CommandLine = new CommandLineSessionState
            {
                State = new CommandLineState(),
                Completion = new CommandCompletionState(),
            },
            Menu = new MenuSessionState
            {
                State = new(),
            },
            Mouse = new MouseSessionState(),
        };
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
