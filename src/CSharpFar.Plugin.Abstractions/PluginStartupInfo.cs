using CSharpFar.Core.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.Plugin.Abstractions;

public sealed record PluginStartupInfo
{
    public required PluginUiServices Ui { get; init; }
    public required PluginSettingsService Settings { get; init; }
    public ICredentialStore? Credentials { get; init; }
    public required IPluginPanelHost Panels { get; init; }
    public IPluginCommandService? Commands { get; init; }
}
