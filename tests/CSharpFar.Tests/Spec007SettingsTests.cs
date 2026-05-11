using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Settings: verifies default values and model structure introduced in Spec 007.
/// </summary>
public sealed class Spec007SettingsTests
{
    [Fact]
    public void DefaultOptions_PreserveCurrentBehavior()
    {
        var opts = new AppSettings.PanelOptionsSettings();

        Assert.True(opts.ShowHiddenAndSystemFiles);
        Assert.True(opts.SelectFolders);
        Assert.True(opts.RightClickSelectsFiles);
        Assert.True(opts.SortFoldersByExtension);
        Assert.True(opts.ShowStatusLine);
        Assert.True(opts.ShowFilesTotalInformation);
        Assert.False(opts.ShowFreeSize);
        Assert.True(opts.ShowSortModeLetter);
        Assert.False(opts.ShowParentDirectoryInRootFolders);
        Assert.False(opts.DetectVolumeMountPoints);
    }

    [Fact]
    public void DefaultAutoRefresh_DisabledThresholdAndNetwork()
    {
        var ar = new AppSettings.PanelAutoRefreshSettings();

        Assert.Equal(0, ar.DisableIfObjectCountExceeds);
        Assert.False(ar.NetworkDrivesAutoRefresh);
    }

    [Fact]
    public void PanelsSettings_HasOptions_WithCorrectType()
    {
        var panels = new AppSettings.PanelsSettings();

        Assert.NotNull(panels.Options);
        Assert.IsType<AppSettings.PanelOptionsSettings>(panels.Options);
    }

    [Fact]
    public void AppSettings_HasPanelsOptions_AsSubProperty()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.Panels.Options);
        Assert.NotNull(settings.Panels.Options.AutoRefresh);
    }
}
