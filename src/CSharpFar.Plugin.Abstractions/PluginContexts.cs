using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Abstractions;

public enum PluginRunMode
{
    Normal = 0,
    Demo = 1,
}

public enum PluginCommandOrigin
{
    PluginsMenu,
    DiskMenu,
    CommandLine,
}

public interface IPluginApplicationContext
{
    PluginRunMode RunMode { get; }
    PanelSide ActivePanelSide { get; }
    string CurrentDirectory { get; }
}

public interface IPluginCommandContext
{
    IPluginApplicationContext Application { get; }
    IPluginSettingsService Settings { get; }
    IPluginUiService Ui { get; }
    IPluginCredentialService Credentials { get; }
    PanelSide TargetPanelSide { get; }
    PluginCommandOrigin Origin { get; }
    string? RawCommandLine { get; }
    string? Arguments { get; }
}

public interface IPanelProviderContext
{
    PanelSide TargetPanelSide { get; }
    IPluginApplicationContext Application { get; }
    IPluginSettingsService Settings { get; }
    IPluginUiService Ui { get; }
    IPluginCredentialService Credentials { get; }
}
