using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.Abstractions;

public interface IPluginApplicationContext
{
    PanelSide ActivePanelSide { get; }

    string CurrentDirectory { get; }
}

public interface IPluginCommandContext
{
    IPluginApplicationContext Application { get; }

    IPluginSettingsService Settings { get; }

    IPluginUiService Ui { get; }
}

public interface IPanelProviderContext
{
    PanelSide TargetPanelSide { get; }

    IPluginApplicationContext Application { get; }

    IPluginSettingsService Settings { get; }

    IPluginUiService Ui { get; }
}
