using CSharpFar.Core.Abstractions;
using CSharpFar.Ui;

namespace CSharpFar.Module.Abstractions;

public sealed record ModuleStartupInfo
{
    public required ModuleUiServices Ui { get; init; }
    public required ModuleSettingsService Settings { get; init; }
    public ICredentialStore? Credentials { get; init; }
    public required IModulePanelHost Panels { get; init; }
}
