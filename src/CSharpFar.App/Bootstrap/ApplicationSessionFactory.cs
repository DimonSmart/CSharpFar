using CSharpFar.App.CommandLine;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using AppSettingsAlias = CSharpFar.Core.Models.AppSettings;

namespace CSharpFar.App.Bootstrap;

internal static class ApplicationSessionFactory
{
    public static ApplicationSession Create(
        AppSettingsAlias settings,
        PanelController controller,
        IFileSystemService fileSystem,
        ApplicationRunOptions? runOptions = null)
    {
        runOptions ??= ApplicationRunOptions.Normal;
        PanelLocation leftStart = ResolveStartLocation(
            settings.Panels.LeftStartDirectory,
            fileSystem,
            runOptions);
        PanelLocation rightStart = ResolveStartLocation(
            settings.Panels.RightStartDirectory,
            fileSystem,
            runOptions);
        var sortMode = ResolveSortMode(settings.Panels.DefaultSortMode);

        var left = new FilePanelState { SortMode = sortMode };
        var right = new FilePanelState { SortMode = sortMode };
        var options = settings.Panels.Options;
        controller.LoadLocation(left, leftStart, options);
        controller.LoadLocation(right, rightStart, options);

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

    private static PanelLocation ResolveStartLocation(
        string? configured,
        IFileSystemService fileSystem,
        ApplicationRunOptions runOptions)
    {
        if (runOptions.Mode == ApplicationRunMode.Demo)
            return PanelLocation.Demo("/");

        string fallback = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(configured) && fileSystem.DirectoryExists(configured))
            return PanelLocation.Local(configured);
        return PanelLocation.Local(fallback);
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
