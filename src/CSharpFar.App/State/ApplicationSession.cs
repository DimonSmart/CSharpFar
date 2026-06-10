using CSharpFar.App.FunctionKeys;

namespace CSharpFar.App.State;

internal sealed class ApplicationSession
{
    public required ApplicationState App { get; init; }

    public required UiTransientState Ui { get; init; }

    public required PanelSessionState Panels { get; init; }

    public required CommandLineSessionState CommandLine { get; init; }

    public required MenuSessionState Menu { get; init; }

    public required MouseSessionState Mouse { get; init; }

    public FunctionKeyLayer FunctionKeyLayer { get; set; } = FunctionKeyLayer.Plain;
}
